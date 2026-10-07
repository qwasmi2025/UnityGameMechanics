// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUInstancerPro.TerrainModule
{
    [DisallowMultipleComponent]
    public abstract class GPUITerrain : MonoBehaviour, IEquatable<GPUITerrain>
    {
        #region Serialized Properties
        [SerializeField]
        protected Bounds _bounds;
        [SerializeField]
        protected Texture2D[] _bakedDetailTextures;
        [SerializeField]
        public bool isAutoFindTreeManager = true;
        [SerializeField]
        public bool isAutoFindDetailManager = true;
        [SerializeField]
        public GPUITerrainHolesSampleMode terrainHolesSampleMode = GPUITerrainHolesSampleMode.Initialization;
        [SerializeField]
        public bool enableDetailDensityReduction = true;
        #endregion Serialized Properties

        #region Runtime Properties
        [NonSerialized]
        protected Transform _cachedTransform;
        [NonSerialized]
        protected Matrix4x4 _cachedLTW = Matrix4x4.zero;
        [NonSerialized]
        protected Vector3 _cachedTerrainPosition = Vector3.zero;
        [NonSerialized]
        protected Matrix4x4 _cachedRotationMatrix = Matrix4x4.identity;
        [NonSerialized]
        public Matrix4x4 deltaLTW = Matrix4x4.identity;
        [NonSerialized]
        protected Bounds _cachedWorldBounds;
        [NonSerialized]
        protected RenderTexture _heightmapTexture;
        [NonSerialized]
        protected TreeInstance[] _treeInstances;
        [NonSerialized]
        protected RenderTexture[] _detailDensityTextures;
        [NonSerialized]
        protected IGPUIProceduralDetailModifier _proceduralDetailModifier;

        public GPUITreeManager TreeManager { get; private set; }
        protected TreePrototype[] _treePrototypes;
        public TreePrototype[] TreePrototypes => _treePrototypes;
        internal int[] TreePrototypeIndexes { get; private set; }
        public int[] TreeInstanceCounts { get => _treeInstanceCounts; private set => _treeInstanceCounts = value; }
        private int[] _treeInstanceCounts;

        public GPUIDetailManager DetailManager { get; private set; }
        protected DetailPrototype[] _detailPrototypes;
        public DetailPrototype[] DetailPrototypes => _detailPrototypes;
        internal int[] DetailPrototypeIndexes { get; private set; }

        public bool IsInitialized { get; private set; }
        public bool IsDetailDensityTexturesLoaded { get; protected set; }
        public bool IsTreeInstancesLoaded { get; protected set; }
        public bool IsTreeInstanceCountsCalculated { get; protected set; }

        public static readonly TreeInstance[] EMPTY_TREE_INSTANCES = new TreeInstance[0];
        public static RenderTexture DUMMY_HOLES_TEXTURE;

#if UNITY_EDITOR
        public bool editor_IsDisableTreeRendering { get; protected set; }
        public bool editor_IsDisableDetailRendering { get; protected set; }
#endif

        internal static List<GPUITerrain> _terrainsSearchingForTreeManager;
        internal static List<GPUITerrain> _terrainsSearchingForDetailManager;

        [NonSerialized]
        protected Transform _treeCollidersParent;
        #endregion Runtime Properties

        #region MonoBehaviour Methods
        protected virtual void Awake()
        {
            if (!GPUIRuntimeSettings.IsSupportedPlatform())
            {
                enabled = false;
                return;
            }
            LoadTerrain();
        }

        protected virtual void OnEnable()
        {
            if (!IsInitialized)
                Initialize();
            if (DetailManager != null)
                DetailManager.RequireUpdate();
            if (TreeManager != null)
                TreeManager.RequireUpdate();
        }

        protected virtual void OnDisable()
        {
            Dispose();
        }
        #endregion MonoBehaviour Methods

        #region Initialize/Dispose

        /// <summary>
        /// Set terrain references and bounds
        /// </summary>
        public virtual void LoadTerrain()
        {
            if (_cachedTransform == null)
                _cachedTransform = transform;
            if (_treePrototypes == null)
                _treePrototypes = new TreePrototype[0];
            if (_detailPrototypes == null)
                _detailPrototypes = new DetailPrototype[0];
            SetTerrainBounds();
            NotifyTransformChanges();
        }

        public virtual bool LoadTerrainData()
        {
            LoadTerrain();
            return true;
        }

        public virtual void ReloadTerrainData()
        {
            if (IsInitialized)
                Initialize();
            if (DetailManager != null)
            {
                DetailManager.OnTerrainsModified();
                DetailManager.RequireUpdate();
            }
            if (TreeManager != null)
            {
                TreeManager.OnTerrainsModified();
                TreeManager.RequireUpdate();
            }
#if UNITY_EDITOR
            GPUIRenderingSystem.Editor_RenderEditModeCameras();
#endif
        }

        protected virtual void Initialize()
        {
            Dispose();
            if (!LoadTerrainData())
                return;
            CreateHeightmapTexture();
            IsInitialized = true;

            if (TreeManager != null)
            {
                if (!TreeManager.AddTerrain(this))
                    SetTreeManager(TreeManager); // if terrain is already added, reload data
            }
            else if (Application.isPlaying && isAutoFindTreeManager)
                AutoFindTreeManager();

            if (DetailManager != null)
            {
                if (!DetailManager.AddTerrain(this))
                    SetDetailManager(DetailManager); // if terrain is already added, reload data
            }
            else if (Application.isPlaying && isAutoFindDetailManager)
                AutoFindDetailManager();
        }

        protected virtual void Dispose()
        {
            IsInitialized = false;
            DisposeDetailDensityTextures();
            DisposeHeightmapTexture();
            DisposeHolesTexture();
            DisposeTreeColliders();
            DisposeTreeInstances();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!gameObject.scene.isLoaded)
                    return;
                if (TreeManager != null)
                    TreeManager.RequireUpdate();
                if (DetailManager != null)
                    DetailManager.RequireUpdate();
                return;
            }
#endif
            if (TreeManager != null)
                TreeManager.RemoveTerrain(this);
            else if (_terrainsSearchingForTreeManager != null)
            {
                int index = _terrainsSearchingForTreeManager.IndexOf(this);
                if (index >= 0)
                    _terrainsSearchingForTreeManager.RemoveAt(index);
            }
            if (DetailManager != null)
                DetailManager.RemoveTerrain(this);
            else if (_terrainsSearchingForDetailManager != null)
            {
                int index = _terrainsSearchingForDetailManager.IndexOf(this);
                if (index >= 0)
                    _terrainsSearchingForDetailManager.RemoveAt(index);
            }

            if (DUMMY_HOLES_TEXTURE != null)
                DUMMY_HOLES_TEXTURE.DestroyRenderTexture();
        }

        public void AutoFindTreeManager()
        {
            if (!GPUIRenderingSystem.IsActive)
                return;
            if (TreeManager == null)
            {
                foreach (GPUIManager manager in GPUIRenderingSystem.Instance.ActiveGPUIManagers) // First check active managers.
                {
                    if (manager is GPUITreeManager treeManager && treeManager != null)
                    {
                        TreeManager = treeManager;
                        if (!TreeManager.AddTerrain(this))
                            SetTreeManager(TreeManager); // if terrain is already added, reload data
                        return;
                    }
                }
                _terrainsSearchingForTreeManager ??= new();
                _terrainsSearchingForTreeManager.Add(this); // If there are no active managers, add it to a list that will be processed when the manager is initialized.
            }
        }

        public void AutoFindDetailManager()
        {
            if (!GPUIRenderingSystem.IsActive)
                return;
            if (DetailManager == null)
            {
                foreach (GPUIManager manager in GPUIRenderingSystem.Instance.ActiveGPUIManagers) // First check active managers.
                {
                    if (manager is GPUIDetailManager detailManager && detailManager != null)
                    {
                        DetailManager = detailManager;
                        if (!DetailManager.AddTerrain(this))
                            SetDetailManager(DetailManager); // if terrain is already added, reload data
                        return;
                    }
                }
                _terrainsSearchingForDetailManager ??= new();
                _terrainsSearchingForDetailManager.Add(this); // If there are no active managers, add it to a list that will be processed when the manager is initialized.
            }
        }

        protected void DisposeDetailDensityTextures()
        {
            IsDetailDensityTexturesLoaded = false;
            if (_detailDensityTextures != null)
            {
                for (int i = 0; i < _detailDensityTextures.Length; i++)
                    DisposeDetailDensityTexture(i);
                _detailDensityTextures = null;
            }
        }

        protected void DisposeTreeInstances()
        {
            IsTreeInstancesLoaded = false;
            _treeInstances = null;
            IsTreeInstanceCountsCalculated = false;
            _treeInstanceCounts = null;
        }

        protected virtual void DisposeHeightmapTexture() { }

        protected virtual void DisposeHolesTexture() { }

        protected void DisposeDetailDensityTexture(int index)
        {
            RenderTexture rt = _detailDensityTextures[index];
            if (rt != null && rt.name.Contains(GPUITerrainConstants.NAME_SUFFIX_DETAILTEXTURE))
            {
                GPUITextureUtility.DestroyRenderTexture(rt);
                _detailDensityTextures[index] = null;
            }
        }

        internal virtual void SetTerrainDetailObjectDistance(float value) { }

        internal virtual void SetTerrainTreeDistance(float value) { }

        #region Create Heightmap and Detail Textures

        public void CreateHeightmapTexture()
        {
            _heightmapTexture = LoadHeightmapTexture();
        }

        protected abstract RenderTexture LoadHeightmapTexture();

        public void CreateDetailTextures()
        {
            LoadDetailDensityTextures();
            IsDetailDensityTexturesLoaded = true;
        }

        protected virtual void LoadDetailDensityTextures()
        {
            DetermineDetailPrototypeIndexes(DetailManager);
            int detailCount = DetailPrototypes == null ? 0 : DetailPrototypes.Length;
            ResizeDetailDensityTextureArray(detailCount);

            if (detailCount == 0)
                return;

            bool isBakedTextures = IsBakedDetailTextures();
            for (int i = 0; i < detailCount; i++)
            {
                CreateDetailTexture(name, i);
                if (!IsReadTerrainDetails(i))
                {
                    _detailDensityTextures[i].ClearRenderTexture();
                    continue;
                }
                if (isBakedTextures)
                    BlitBakedDetailTexture(i);
            }
            ExecuteProceduralDetails();

            RequireDetailUpdate(!Application.isPlaying);
        }

        protected void ExecuteProceduralDetails()
        {
            if (DetailManager != null)
                DetailManager.ExecuteProceduralDetails(this);
            if (_proceduralDetailModifier != null)
                _proceduralDetailModifier.Execute(this);
        }

        protected virtual void CreateDetailTexture(string terrainName, int index)
        {
            if (_detailDensityTextures[index] == null)
                _detailDensityTextures[index] = GPUITerrainUtility.CreateDetailRenderTexture(GetDetailResolution(), terrainName + GPUITerrainConstants.NAME_SUFFIX_DETAILTEXTURE + index);
        }

        protected void ResizeDetailDensityTextureArray(int detailCount)
        {
            int detailResolution = GetDetailResolution();

            if (_detailDensityTextures == null)
                _detailDensityTextures = new RenderTexture[detailCount];
            else if (_detailDensityTextures.Length != detailCount)
            {
                for (int i = detailCount; i < _detailDensityTextures.Length; i++)
                    DisposeDetailDensityTexture(i);
                Array.Resize(ref _detailDensityTextures, detailCount);
            }

            if (IsBakedDetailTextures())
                ResizeBakedDetailTextureArray(detailCount);

            for (int i = 0; i < detailCount; i++)
            {
                RenderTexture rt = _detailDensityTextures[i];
                if (rt != null && rt.width != detailResolution)
                {
                    DisposeDetailDensityTexture(i);
                    rt = null;
                }
                if (rt == null)
                    CreateDetailTexture(name, i);
            }
        }

        protected virtual void ResizeBakedDetailTextureArray(int detailCount)
        {
            if (_bakedDetailTextures == null)
            {
                _bakedDetailTextures = new Texture2D[detailCount];
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    EditorUtility.SetDirty(gameObject);
#endif
            }
            else if (_bakedDetailTextures.Length != detailCount)
            {
                Array.Resize(ref _bakedDetailTextures, detailCount);
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    EditorUtility.SetDirty(gameObject);
#endif
            }
        }

        protected void BlitBakedDetailTexture(int index)
        {
            Texture2D bakedTexture = GetBakedDetailTexture(index);
            if (bakedTexture != null)
                GPUITextureUtility.CopyTextureSamplerWithComputeShader(bakedTexture, _detailDensityTextures[index]);
            else
                _detailDensityTextures[index].ClearRenderTexture(); // If no texture is provided, clear the render texture.
        }

        protected abstract int GetDetailResolution();

        #endregion Create Heightmap and Detail Textures

        protected virtual void DisposeTreeColliders()
        {
            if (_treeCollidersParent != null)
                _treeCollidersParent.gameObject.DestroyGeneric();
            _treeCollidersParent = null;
        }

        #endregion Initialize/Dispose

        #region Update Methods
        internal void GenerateVegetation(GPUIDetailPrototypeData detailPrototypeData, GraphicsBuffer transformBuffer, GPUIDataBuffer<uint> counterBuffer, Vector3 cameraPos, float detailObjectDistance, Texture2D healthyDryNoiseTexture, int[] sizeAndIndexes, bool isCheckTerrainBounds = false)
        {
            if (!IsInitialized) return;

            if (_heightmapTexture == null)
            {
                CreateHeightmapTexture();
                if (_heightmapTexture == null)
                    return;
            }

            if (!IsDetailDensityTexturesLoaded)
                CreateDetailTextures();

            if (_detailDensityTextures == null) return;

            Vector3 terrainPos = GetPosition();
            if (isCheckTerrainBounds && !IsTerrainWithinViewDistance(cameraPos, detailObjectDistance))
                return;
            Vector3 terrainSize = GetSize();

            if (DetailPrototypeIndexes == null)
                DetermineDetailPrototypeIndexes(DetailManager);

            int prototypeIndex = sizeAndIndexes[1];
            int subSettingCount = detailPrototypeData.GetSubSettingCount();

            Texture holesTexture = null;
            if (terrainHolesSampleMode == GPUITerrainHolesSampleMode.Runtime)
                holesTexture = GetHolesTexture();

            bool hasMatrixOffset = false;
            Matrix4x4 rotationMatrix = GPUIConstants.IDENTITY_Matrix4x4;
            if (HasRotationSupport())
            {
                rotationMatrix = GetRotationMatrix();
                hasMatrixOffset = !rotationMatrix.EqualsMatrix4x4(GPUIConstants.IDENTITY_Matrix4x4);
            }

            ComputeShader CS_VegetationGenerator = GPUITerrainConstants.CS_VegetationGenerator;

            if (HasTwoChannelHeightmap())
                CS_VegetationGenerator.EnableKeyword(GPUITerrainConstants.Kw_GPUI_TWO_CHANNEL_HEIGHTMAP);
            else
                CS_VegetationGenerator.DisableKeyword(GPUITerrainConstants.Kw_GPUI_TWO_CHANNEL_HEIGHTMAP);

            for (int terrainPrototypeIndex = 0; terrainPrototypeIndex < DetailPrototypeIndexes.Length && terrainPrototypeIndex < _detailDensityTextures.Length; terrainPrototypeIndex++)
            {
                int managerPrototypeIndex = DetailPrototypeIndexes[terrainPrototypeIndex];
                if (managerPrototypeIndex % GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER != prototypeIndex) continue;
                if (!detailPrototypeData.TryGetParameterBufferIndex(out sizeAndIndexes[2])) continue;
                int subSettingIndex = managerPrototypeIndex / GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER;
                if (subSettingCount <= subSettingIndex || !detailPrototypeData.GetSubSettings(subSettingIndex).TryGetParameterBufferIndex(out sizeAndIndexes[3]))
                {
                    Debug.LogError(GPUIConstants.LOG_PREFIX + "Can not find Detail Prototype Sub Setting parameter buffer index.");
                    continue;
                }

                RenderTexture detailTexture = _detailDensityTextures[terrainPrototypeIndex];
                if (detailTexture == null) continue;

                int detailTextureWidth = detailTexture.width;
                if (enableDetailDensityReduction && detailPrototypeData.isUseDensityReduction && detailPrototypeData.densityReduceDistance < detailObjectDistance)
                    CS_VegetationGenerator.EnableKeyword(GPUITerrainConstants.Kw_GPUI_DETAIL_DENSITY_REDUCE);
                else
                    CS_VegetationGenerator.DisableKeyword(GPUITerrainConstants.Kw_GPUI_DETAIL_DENSITY_REDUCE);

                if (terrainHolesSampleMode == GPUITerrainHolesSampleMode.Runtime && holesTexture != null)
                {
                    CS_VegetationGenerator.EnableKeyword(GPUITerrainConstants.Kw_GPUI_TERRAIN_HOLES);
                    CS_VegetationGenerator.SetTexture(0, GPUITerrainConstants.PROP_terrainHoleTexture, holesTexture);
                }
                else
                    CS_VegetationGenerator.DisableKeyword(GPUITerrainConstants.Kw_GPUI_TERRAIN_HOLES);

                CS_VegetationGenerator.SetBuffer(0, GPUIConstants.PROP_gpuiTransformBuffer, transformBuffer);
                CS_VegetationGenerator.SetBuffer(0, GPUITerrainConstants.PROP_detailCounterBuffer, counterBuffer);

                CS_VegetationGenerator.SetBuffer(0, GPUIConstants.PROP_parameterBuffer, GPUIRenderingSystem.Instance.ParameterBuffer);
                CS_VegetationGenerator.SetTexture(0, GPUITerrainConstants.PROP_terrainDetailTexture, detailTexture);
                CS_VegetationGenerator.SetTexture(0, GPUITerrainConstants.PROP_heightmapTexture, _heightmapTexture);

                CS_VegetationGenerator.SetInt(GPUITerrainConstants.PROP_detailTextureSize, detailTextureWidth);
                CS_VegetationGenerator.SetInt(GPUITerrainConstants.PROP_heightmapTextureSize, _heightmapTexture.width);
                CS_VegetationGenerator.SetVector(GPUITerrainConstants.PROP_startPosition, terrainPos);
                CS_VegetationGenerator.SetVector(GPUITerrainConstants.PROP_terrainSize, terrainSize);
                CS_VegetationGenerator.SetInts(GPUIConstants.PROP_sizeAndIndexes, sizeAndIndexes);

                CS_VegetationGenerator.SetVector(GPUITerrainConstants.PROP_cameraPos, cameraPos);
                CS_VegetationGenerator.SetFloat(GPUITerrainConstants.PROP_density, GetDetailDensity(terrainPrototypeIndex));
                CS_VegetationGenerator.SetFloat(GPUITerrainConstants.PROP_detailObjectDistance, detailObjectDistance);

                CS_VegetationGenerator.SetTexture(0, GPUITerrainConstants.PROP_healthyDryNoiseTexture, healthyDryNoiseTexture);

                if (hasMatrixOffset)
                {
                    CS_VegetationGenerator.EnableKeyword(GPUIConstants.Kw_GPUI_TRANSFORM_OFFSET);
                    CS_VegetationGenerator.SetMatrix(GPUIConstants.PROP_gpuiTransformOffset, rotationMatrix);
                }
                else
                    CS_VegetationGenerator.DisableKeyword(GPUIConstants.Kw_GPUI_TRANSFORM_OFFSET);

                CS_VegetationGenerator.DispatchXY(0, detailTextureWidth, detailTextureWidth);
            }
        }

        public bool IsTerrainWithinViewDistance(Vector3 cameraPos, float detailObjectDistance)
        {
            Bounds b = GetTerrainWorldBounds();
            if (!b.Contains(cameraPos) && Mathf.Sqrt(b.SqrDistance(cameraPos)) > detailObjectDistance)
                return false;

            return true;
        }

        private int _lastTerrainTransformChangeCheckFrame = -1;
        /// <summary>
        /// Runs every update to check for transform changes.
        /// </summary>
        public virtual void NotifyTransformChanges()
        {
            int frameCount = Time.frameCount;
            if (_lastTerrainTransformChangeCheckFrame == frameCount)
                return;
            _lastTerrainTransformChangeCheckFrame = frameCount;

            Matrix4x4 previousLTW = _cachedLTW;
            _cachedLTW = _cachedTransform.localToWorldMatrix;
            if (_cachedLTW.EqualsMatrix4x4(previousLTW))
                return;

            bool hasRotationSupport = HasRotationSupport();
            if (hasRotationSupport)
                _cachedRotationMatrix = GPUIUtility.GetRotationMatrix(GetTerrainRotation());

            _cachedTerrainPosition = GetCalculatedTerrainPosition();
            _cachedWorldBounds = GetCalculatedTerrainWorldBounds();

            bool hasScalingChange = false;
            if (HasScalingSupport())
            {
                if (!previousLTW.EqualsMatrix4x4(Matrix4x4.zero) && previousLTW.lossyScale != _cachedLTW.lossyScale)
                {
                    hasScalingChange = true;
                    if (_treeCollidersParent != null)
                        CreateTreeColliders();
                    RequireTreeUpdate();
                    RequireDetailUpdate(true);
                }
            }

            if (!hasScalingChange)
            {
                if (hasRotationSupport)
                    deltaLTW = _cachedLTW * previousLTW.inverse;
                else
                {
                    deltaLTW = GPUIConstants.IDENTITY_Matrix4x4;
                    deltaLTW.SetPosition(_cachedLTW.GetPosition() - previousLTW.GetPosition());
                }

                if (TreeManager != null)
                    TreeManager.RequireTerrainTransformChange(frameCount);
                if (DetailManager != null)
                    DetailManager.RequireTerrainTransformChange(frameCount);
            }
        }

        /// <summary>
        /// Loads and stores the TreeInstance array.
        /// </summary>
        protected virtual void LoadTreeInstances()
        {
            IsTreeInstancesLoaded = true;
            IsTreeInstanceCountsCalculated = false;
        }

        protected void ConvertToGPUITreeData(GPUITreeManager treeManager)
        {
            if (treeManager._enableTreeInstanceColors)
            {
                TreeInstance[] treeInstances = GetTreeInstances();
                for (int i = 0; i < treeInstances.Length; i++)
                {
                    TreeInstance treeInstance = treeInstances[i];
                    Color color = treeInstance.color;
                    treeInstance.color = GPUIUtility.DecodeFloatRGBA(color);
                    treeInstances[i] = treeInstance;
                }
            }
        }

        public void RequireTreeUpdate(bool reloadTreeInstances = false)
        {
            if (TreeManager != null)
                TreeManager.RequireUpdate(reloadTreeInstances);
        }

        public void RequireDetailUpdate(bool forceImmediateUpdate = false, bool reloadTerrainDetailTextures = false)
        {
            if (DetailManager != null)
                DetailManager.RequireUpdate(forceImmediateUpdate, reloadTerrainDetailTextures);
        }

        protected virtual void CreateTreeColliders() { }

        #endregion Update Methods

        #region Getters / Setters

        #region Prototype Management

        public void DetermineTreePrototypeIndexes(GPUITreeManager treeManager)
        {
            if (treeManager == null) return;
            IsTreeInstanceCountsCalculated = false;
            if (TreePrototypes == null)
            {
                if (TreePrototypeIndexes == null || TreePrototypeIndexes.Length != 0)
                    TreePrototypeIndexes = new int[0];
                return;
            }
            if (TreePrototypeIndexes == null || TreePrototypeIndexes.Length != TreePrototypes.Length)
                TreePrototypeIndexes = new int[TreePrototypes.Length];

            for (int i = 0; i < TreePrototypes.Length; i++)
                TreePrototypeIndexes[i] = treeManager.DetermineTreePrototypeIndex(TreePrototypes[i]);
        }

        public void DetermineDetailPrototypeIndexes(GPUIDetailManager detailManager)
        {
            if (detailManager == null) return;
            if (DetailPrototypes == null)
            {
                DetailPrototypeIndexes = new int[0];
                return;
            }
            if (DetailPrototypeIndexes == null || DetailPrototypeIndexes.Length != DetailPrototypes.Length)
                DetailPrototypeIndexes = new int[DetailPrototypes.Length];

            for (int i = 0; i < DetailPrototypes.Length; i++)
                DetailPrototypeIndexes[i] = detailManager.DetermineDetailPrototypeIndex(DetailPrototypes[i]);
        }

        public int GetFristTerrainTreePrototypeIndex(int managerPrototypeIndex)
        {
            if (TreePrototypeIndexes == null)
                return -1;

            for (int i = 0; i < TreePrototypeIndexes.Length; i++)
            {
                if (TreePrototypeIndexes[i] == managerPrototypeIndex)
                    return i;
            }

            return -1;
        }

        public void GetTerrainTreePrototypeIndexes(int managerPrototypeIndex, ref List<int> terrainPrototypeIndexes)
        {
            terrainPrototypeIndexes.Clear();
            if (TreePrototypeIndexes == null)
                return;

            for (int i = 0; i < TreePrototypeIndexes.Length; i++)
            {
                if (TreePrototypeIndexes[i] == managerPrototypeIndex)
                    terrainPrototypeIndexes.Add(i);
            }
        }

        public int GetFirstTerrainDetailPrototypeIndex(int managerPrototypeIndex)
        {
            if (DetailPrototypeIndexes == null)
                return -1;
            for (int i = 0; i < DetailPrototypeIndexes.Length; i++)
            {
                if (DetailPrototypeIndexes[i] % GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER == managerPrototypeIndex)
                    return i;
            }
            return -1;
        }

        public void GetTerrainDetailPrototypeIndexes(int managerPrototypeIndex, ref List<int> terrainPrototypeIndexes)
        {
            if (terrainPrototypeIndexes == null)
                terrainPrototypeIndexes ??= new List<int>();
            else
                terrainPrototypeIndexes.Clear();
            if (DetailPrototypeIndexes == null)
                return;

            for (int i = 0; i < DetailPrototypeIndexes.Length; i++)
            {
                if (DetailPrototypeIndexes[i] % GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER == managerPrototypeIndex)
                    terrainPrototypeIndexes.Add(i);
            }
        }

        protected bool IsUnorderedTreePrototypeIndexes(GPUITreeManager treeManager)
        {
            if (TreePrototypeIndexes == null)
                DetermineTreePrototypeIndexes(treeManager);
            for (int i = 0; i < TreePrototypeIndexes.Length; i++)
            {
                if (i != TreePrototypeIndexes[i])
                    return true;
            }
            return false;
        }

        public string GetTreePrototypeIndexesToString()
        {
            if (TreePrototypeIndexes == null)
                return null;
            string result = "";
            for (int i = 0; i < TreePrototypeIndexes.Length; i++)
            {
                if (i > 0)
                    result += ", ";
                result += TreePrototypeIndexes[i];
            }
            return result;
        }

        public string GetDetailPrototypeIndexesToString()
        {
            if (DetailPrototypeIndexes == null)
                return null;
            string result = "";
            for (int i = 0; i < DetailPrototypeIndexes.Length; i++)
            {
                if (i > 0)
                    result += ", ";
                result += (DetailPrototypeIndexes[i] % GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER) + "[" + (DetailPrototypeIndexes[i] / GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER) + "]";
            }
            return result;
        }

        /// <summary>
        /// Adds the given GameObject as a TreePrototype to the terrain.
        /// </summary>
        public virtual void AddTreePrototypeToTerrain(GameObject pickerGameObject, int overwriteIndex)
        {
            LoadTerrainData();
            if (_treePrototypes == null)
                return;

            if (overwriteIndex >= 0)
            {
                List<int> terrainPrototypeIndexes = new List<int>();
                GetTerrainTreePrototypeIndexes(overwriteIndex, ref terrainPrototypeIndexes);
                foreach (var prototypeIndex in terrainPrototypeIndexes)
                {
                    if (prototypeIndex >= 0 && prototypeIndex < _treePrototypes.Length)
                    {
                        _treePrototypes[prototypeIndex].prefab = pickerGameObject;
                    }
                }
            }
            else
            {
                _treePrototypes = _treePrototypes.AddAndReturn(new TreePrototype()
                {
                    prefab = pickerGameObject
                });
            }

            DetermineTreePrototypeIndexes(TreeManager);
            SaveTreePrototypesToTerrainData();
        }

        /// <summary>
        /// Adds the given object as a DetailPrototype to the terrain.
        /// </summary>
        public virtual void AddDetailPrototypeToTerrain(UnityEngine.Object pickerObject, int overwriteIndex, int noiseSeed = 0)
        {
            LoadTerrainData();
            if (_detailPrototypes == null)
                return;
            if (pickerObject is Texture2D)
            {
                if (overwriteIndex >= 0)
                {
                    List<int> terrainPrototypeIndexes = new List<int>();
                    GetTerrainDetailPrototypeIndexes(overwriteIndex, ref terrainPrototypeIndexes);
                    foreach (var prototypeIndex in terrainPrototypeIndexes)
                    {
                        if (prototypeIndex >= 0 && prototypeIndex < _detailPrototypes.Length)
                        {
                            _detailPrototypes[prototypeIndex].prototype = null;
                            _detailPrototypes[prototypeIndex].prototypeTexture = (Texture2D)pickerObject;
                            _detailPrototypes[prototypeIndex].renderMode = DetailRenderMode.GrassBillboard;
                            _detailPrototypes[prototypeIndex].usePrototypeMesh = false;
                        }
                    }
                }
                else
                {
                    _detailPrototypes = _detailPrototypes.AddAndReturn(new DetailPrototype()
                    {
                        usePrototypeMesh = false,
                        prototypeTexture = (Texture2D)pickerObject,
                        renderMode = DetailRenderMode.GrassBillboard,
                        noiseSeed = noiseSeed == 0 ? UnityEngine.Random.Range(100, 100000) : noiseSeed
                    });
                }
            }
            else if (pickerObject is GameObject pickerGameObject)
            {
                if (pickerGameObject.GetComponentInChildren<MeshRenderer>() == null)
                    return;

                if (overwriteIndex >= 0)
                {
                    List<int> terrainPrototypeIndexes = new List<int>();
                    GetTerrainDetailPrototypeIndexes(overwriteIndex, ref terrainPrototypeIndexes);
                    foreach (var prototypeIndex in terrainPrototypeIndexes)
                    {
                        if (prototypeIndex >= 0 && prototypeIndex < _detailPrototypes.Length)
                        {
                            _detailPrototypes[prototypeIndex].prototype = pickerGameObject.GetComponentInChildren<MeshRenderer>().gameObject;
                            _detailPrototypes[prototypeIndex].prototypeTexture = null;
                            _detailPrototypes[prototypeIndex].renderMode = DetailRenderMode.VertexLit;
                            _detailPrototypes[prototypeIndex].usePrototypeMesh = true;
                        }
                    }
                }
                else
                {
                    _detailPrototypes = _detailPrototypes.AddAndReturn(new DetailPrototype()
                    {
                        usePrototypeMesh = true,
                        prototype = pickerGameObject.GetComponentInChildren<MeshRenderer>().gameObject,
                        renderMode = DetailRenderMode.VertexLit,
                        noiseSeed = noiseSeed == 0 ? UnityEngine.Random.Range(100, 100000) : noiseSeed,
                        healthyColor = Color.white,
                        dryColor = Color.white,
                        useInstancing = true
                    });
                }
            }

            DetermineDetailPrototypeIndexes(DetailManager);
            SaveDetailPrototypesToTerrainData();
        }

        /// <summary>
        /// Adds the tree prototypes on the Tree Manager to the terrain.
        /// </summary>
        [ContextMenu("Add Tree Prototypes From Manager")]
        public void AddAllTreePrototypesFromTreeManager()
        {
            if (TreeManager == null)
                return;

            DetermineTreePrototypeIndexes(TreeManager);
            int managerPrototypeCount = TreeManager.GetPrototypeCount();
            for (int i = 0; i < managerPrototypeCount; i++)
            {
                bool found = false;
                for (int j = 0; j < TreePrototypeIndexes.Length; j++)
                {
                    if (i == TreePrototypeIndexes[j])
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    AddTreePrototypeToTerrain(TreeManager.GetPrototype(i).prefabObject, -1);
                }
            }
        }

        /// <summary>
        /// Adds the detail prototypes on the Detail Manager to the terrain.
        /// </summary>
        [ContextMenu("Add Detail Prototypes From Manager")]
        public void AddAllDetailPrototypesFromDetailManager()
        {
            if (DetailManager == null)
                return;

            DetermineDetailPrototypeIndexes(DetailManager);
            int managerPrototypeCount = DetailManager.GetPrototypeCount();
            int terrainPrototypeCount = _detailPrototypes.Length;
            for (int i = 0; i < managerPrototypeCount; i++)
            {
                bool found = false;
                for (int j = 0; j < DetailPrototypeIndexes.Length; j++)
                {
                    if (i == (DetailPrototypeIndexes[j] % GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var detailPrototypeData = DetailManager.GetPrototypeData(i);
                    DetailPrototype detailPrototype;
                    if (detailPrototypeData.detailTexture != null)
                    {
                        AddDetailPrototypeToTerrain(detailPrototypeData.detailTexture, -1, detailPrototypeData.GetFirstNoiseSeed());
                        detailPrototype = _detailPrototypes[terrainPrototypeCount];
                        detailPrototype.renderMode = detailPrototypeData.isBillboard ? DetailRenderMode.GrassBillboard : DetailRenderMode.Grass;
                    }
                    else
                    {
                        AddDetailPrototypeToTerrain(DetailManager.GetPrototype(i).prefabObject, -1, detailPrototypeData.GetFirstNoiseSeed());
                        detailPrototype = _detailPrototypes[terrainPrototypeCount];
                        detailPrototype.renderMode = DetailRenderMode.VertexLit;
                    }
                    detailPrototype.dryColor = detailPrototypeData.dryColor;
                    detailPrototype.healthyColor = detailPrototypeData.healthyColor;
                    detailPrototype.noiseSpread = detailPrototypeData.noiseSpread;

                    if (detailPrototypeData.GetSubSettingCount() > 0)
                    {
                        var subSettings = detailPrototypeData.GetSubSettings(0);
                        detailPrototype.alignToGround = subSettings.alignToGround;
                        if (subSettings.maxHeight > 0)
                            detailPrototype.maxHeight = subSettings.maxHeight;
                        if (subSettings.maxWidth > 0)
                            detailPrototype.maxWidth = subSettings.maxWidth;
                        if (subSettings.minHeight > 0)
                            detailPrototype.minHeight = subSettings.minHeight;
                        if (subSettings.maxHeight > 0)
                            detailPrototype.maxHeight = subSettings.maxHeight;
                        if (subSettings.noiseSeed != 0)
                            detailPrototype.noiseSeed = subSettings.noiseSeed;
                    }

                    SaveDetailPrototypesToTerrainData();

                    terrainPrototypeCount++;
                }
            }
        }

        /// <summary>
        /// Saves changes on the tree prototypes to the terrain data.
        /// </summary>
        protected virtual void SaveTreePrototypesToTerrainData() { }
        /// <summary>
        /// Saves changes on the detail prototypes to the terrain data.
        /// </summary>
        protected virtual void SaveDetailPrototypesToTerrainData() { }

        /// <summary>
        /// Removes the TreePrototype at the given index from terrain data.
        /// </summary>
        public void RemoveTreePrototypeAtIndex(int index)
        {
            LoadTerrainData();
            if (_treePrototypes == null || _treePrototypes.Length == 0)
                return;
            List<int> terrainPrototypeIndexes = new List<int>();
            GetTerrainTreePrototypeIndexes(index, ref terrainPrototypeIndexes);
            LoadTreeInstances();
            foreach (int terrainPrototypeIndex in terrainPrototypeIndexes)
            {
                RemoveTerrainTreePrototypeAtIndex(terrainPrototypeIndex);
            }
            OnRemoveTreePrototypesAtIndexes(terrainPrototypeIndexes);
            DetermineTreePrototypeIndexes(TreeManager);
        }

        private void RemoveTerrainTreePrototypeAtIndex(int terrainPrototypeIndex)
        {
            _treePrototypes = _treePrototypes.RemoveAtAndReturn(terrainPrototypeIndex);
            if (_treeInstances == null || _treeInstances.Length == 0)
                return;
            List<TreeInstance> treeInstanceList = new List<TreeInstance>(_treeInstances);
            for (int i = 0; i < treeInstanceList.Count; i++)
            {
                TreeInstance treeInstance = treeInstanceList[i];
                if (treeInstance.prototypeIndex < terrainPrototypeIndex)
                    continue;
                else if (treeInstance.prototypeIndex == terrainPrototypeIndex)
                {
                    treeInstanceList.RemoveAt(i);
                    i--;
                }
                else if (treeInstance.prototypeIndex > terrainPrototypeIndex)
                {
                    treeInstance.prototypeIndex--;
                    treeInstanceList[i] = treeInstance;
                }
            }
            _treeInstances = treeInstanceList.ToArray();
            IsTreeInstanceCountsCalculated = false;
        }

        protected abstract void OnRemoveTreePrototypesAtIndexes(List<int> terrainPrototypeIndexes);

        /// <summary>
        /// Removes the DetailPrototype at the given index from terrain data.
        /// </summary>
        public void RemoveDetailPrototypeAtIndex(int index)
        {
            DisposeDetailDensityTextures();
            List<int> terrainPrototypeIndexes = new List<int>();
            GetTerrainDetailPrototypeIndexes(index, ref terrainPrototypeIndexes);

            foreach (int terrainPrototypeIndex in terrainPrototypeIndexes)
            {
                _detailPrototypes = _detailPrototypes.RemoveAtAndReturn(terrainPrototypeIndex);
            }
            OnRemoveDetailPrototypesAtIndexes(terrainPrototypeIndexes);
            DetermineDetailPrototypeIndexes(DetailManager);
        }

        protected abstract void OnRemoveDetailPrototypesAtIndexes(List<int> terrainPrototypeIndexes);

        #endregion Prototype Management

        internal void SetTreeManager(GPUITreeManager treeManager)
        {
            Profiler.BeginSample("GPUITerrain.SetTreeManager");
            if (TreeManager != null && TreeManager != treeManager) // To avoid adding the same terrain on multiple managers
                TreeManager.RemoveTerrain(this);
            TreeManager = treeManager;

            SetTerrainTreeDistance(0);
            DetermineTreePrototypeIndexes(treeManager);

            Profiler.EndSample();
        }

        internal void SetDetailManager(GPUIDetailManager detailManager)
        {
            Profiler.BeginSample("GPUITerrain.SetDetailManager");
            if (DetailManager != null && DetailManager != detailManager) // To avoid adding the same terrain on multiple managers
                DetailManager.RemoveTerrain(this);
            DetailManager = detailManager;

            SetTerrainDetailObjectDistance(0);
            DetermineDetailPrototypeIndexes(detailManager);
            Profiler.EndSample();
        }

        internal void RemoveTreeManager()
        {
            if (TreeManager != null && (!Application.isPlaying || TreeManager.isEnableDefaultRenderingWhenDisabled))
                SetTerrainTreeDistance(GetTerrainTreeDistance());
            TreeManager = null;
        }

        internal void RemoveDetailManager()
        {
            if (DetailManager != null && (!Application.isPlaying || DetailManager.isEnableDefaultRenderingWhenDisabled))
                SetTerrainDetailObjectDistance(DetailManager.detailObjectDistance);
            DetailManager = null;
        }

        public virtual float GetTerrainTreeDistance()
        {
            return 5000f;
        }

        public RenderTexture GetHeightmapTexture()
        {
            if (_heightmapTexture == null)
                CreateHeightmapTexture();
            return _heightmapTexture;
        }

        public virtual void SetHeightmapTexture(RenderTexture heightmapTexture)
        {
            _heightmapTexture = heightmapTexture;
            RequireTreeUpdate();
            RequireDetailUpdate(true);
        }

        public abstract int GetHeightmapResolution();

        public virtual bool SetTerrainBounds(bool forceNew = false)
        {
            if (forceNew || _bounds == default)
            {
                _bounds = _cachedTransform.gameObject.GetBounds(true);
                _bounds.center -= _cachedTransform.position;
                if (HasScalingSupport())
                {
                    Vector3 scaleReciprocal = _cachedTransform.lossyScale.Reciprocal();
                    _bounds.extents = Vector3.Scale(_bounds.extents, scaleReciprocal);
                    _bounds.center = Vector3.Scale(_bounds.center, scaleReciprocal);

                    if (_bounds.extents == Vector3.zero)
                        _bounds.extents = Vector3.one;
                }
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    EditorUtility.SetDirty(gameObject);
#endif
                RequireTreeUpdate();
                RequireDetailUpdate();
                return true;
            }
            return false;
        }

        protected virtual Vector3 GetCalculatedTerrainPosition()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (_cachedTransform == null)
                    _cachedTransform = transform;
                _cachedLTW = _cachedTransform.localToWorldMatrix;
            }
#endif
            Vector3 boundsMin = _bounds.min;
            if (HasScalingSupport())
                boundsMin = Vector3.Scale(boundsMin, GetTerrainScale());
            if (HasRotationSupport())
                boundsMin = GetTerrainRotation() * boundsMin;

            return _cachedLTW.GetPosition() + boundsMin;
        }

        /// <returns>The bottom left coordinates of the terrain.</returns>
        public virtual Vector3 GetPosition()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return GetCalculatedTerrainPosition();
#endif
            return _cachedTerrainPosition;
        }

        protected Bounds GetCalculatedTerrainWorldBounds()
        {
            Bounds worldBounds = _bounds;
            if (HasScalingSupport())
            {
                Vector3 scale = GetTerrainScale();
                worldBounds.center = Vector3.Scale(worldBounds.center, scale);
                worldBounds.extents = Vector3.Scale(worldBounds.extents, scale);
            }
            if (HasRotationSupport())
                worldBounds = GPUIUtility.GetMatrixAppliedBounds(worldBounds, GetRotationMatrix());
            worldBounds.center += _cachedTransform.position;

            return worldBounds;
        }

        public virtual Bounds GetTerrainWorldBounds()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return GetCalculatedTerrainWorldBounds();
#endif
            return _cachedWorldBounds;
        }

        public virtual bool IsBakedDetailTextures()
        {
            return true;
        }

        public virtual Vector3 GetSize()
        {
            Vector3 size = _bounds.size;
            if (HasScalingSupport())
                size = Vector3.Scale(size, GetTerrainScale());
            return size;
        }

        public virtual float GetDetailDensity(int prototypeIndex)
        {
            return 255f;
        }

        public int GetDetailTextureCount()
        {
            if (_detailDensityTextures == null)
                return 0;
            return _detailDensityTextures.Length;
        }

        public RenderTexture GetDetailDensityTexture(int index)
        {
            if (_detailDensityTextures == null || index < 0 || _detailDensityTextures.Length <= index)
                return null;
            return _detailDensityTextures[index];
        }

        public virtual int GetBakedDetailTextureCount()
        {
            if (_bakedDetailTextures == null)
                return 0;
            return _bakedDetailTextures.Length;
        }

        public virtual Texture2D GetBakedDetailTexture(int index)
        {
            if (_bakedDetailTextures == null || index < 0 || _bakedDetailTextures.Length <= index)
                return null;
            return _bakedDetailTextures[index];
        }

        public virtual void SetBakedDetailTexture(int index, Texture2D texture)
        {
            if (DetailPrototypes == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Detail prototypes are not set.");
                return;
            }
            if (_bakedDetailTextures == null)
                _bakedDetailTextures = new Texture2D[DetailPrototypes.Length];
            if (index < 0 || index > _bakedDetailTextures.Length)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "SetBakedDetailTexture error: given index [" + index + "] is out of bounds. Detail prototype count: " + _bakedDetailTextures.Length);
                return;
            }
            _bakedDetailTextures[index] = texture;
            if (IsDetailDensityTexturesLoaded)
                CreateDetailTextures();
        }

        /// <summary>
        /// Updates the specified detail layer using a density texture, where the grayscale values define
        /// the per cell detail density for that layer.
        /// </summary>
        /// <param name="index">Detail layer index.</param>
        /// <param name="renderTexture">Grayscale texture encoding detail density.</param>
        public virtual void SetDetailDensityTexture(int index, RenderTexture renderTexture)
        {
            if (DetailPrototypes == null)
            {
                Debug.LogError(GPUIConstants.LOG_PREFIX + "Detail prototypes are not set.");
                return;
            }
            if (!IsDetailDensityTexturesLoaded)
                CreateDetailTextures();
            if (_detailDensityTextures[index] == renderTexture)
                return;
            DisposeDetailDensityTexture(index);
            _detailDensityTextures[index] = renderTexture;
        }

        public TreeInstance[] GetTreeInstances(bool reloadTreeInstances = false)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && !isActiveAndEnabled)
                return EMPTY_TREE_INSTANCES;
#endif
            if (reloadTreeInstances || !IsTreeInstancesLoaded)
            {
                LoadTreeInstances();
            }
            if (!IsTreeInstanceCountsCalculated)
                CalculateTreeInstanceCounts();
            if (_treeInstances == null)
                return EMPTY_TREE_INSTANCES;
            return _treeInstances;
        }

        public void CalculateTreeInstanceCounts()
        {
            IsTreeInstanceCountsCalculated = true;
            int treePrototypeCount = TreePrototypes.Length;
            if (_treeInstanceCounts == null || _treeInstanceCounts.Length != treePrototypeCount)
                _treeInstanceCounts = new int[treePrototypeCount];
            else
            {
                for (int i = 0; i < treePrototypeCount; i++)
                    _treeInstanceCounts[i] = 0;
            }
            if (_treeInstances == null)
                return;

            int treeDataSize = _treeInstances.Length;
            for (int i = 0; i < treeDataSize; i++)
            {
                int treeDataPrototypeIndex = _treeInstances[i].prototypeIndex;
                if (treeDataPrototypeIndex >= treePrototypeCount || treeDataPrototypeIndex < 0)
                    continue;
                _treeInstanceCounts[treeDataPrototypeIndex]++;
            }
        }

        public virtual void SetTreeInstances(TreeInstance[] treeInstances, bool applyToTerrainData = false)
        {
            IsTreeInstancesLoaded = true;
            IsTreeInstanceCountsCalculated = false;
            _treeInstances = treeInstances;
            if (TreeManager != null)
            {
                ConvertToGPUITreeData(TreeManager);
                TreeManager.RequireUpdate();
            }
        }

        public virtual Color GetWavingGrassTint()
        {
            return Color.white;
        }

        public bool Equals(GPUITerrain other)
        {
            if (other == null) return false;
            return this.GetInstanceIDGPUI() == other.GetInstanceIDGPUI();
        }

        public override bool Equals(object obj)
        {
            if (obj is GPUITerrain other)
                return Equals(other);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return this.GetInstanceIDGPUI();
        }

        public virtual Texture GetHolesTexture()
        {
            if (DUMMY_HOLES_TEXTURE == null)
            {
                DUMMY_HOLES_TEXTURE = new RenderTexture(1, 1, 0, GPUITerrainConstants.R8_RenderTextureFormat, RenderTextureReadWrite.Linear)
                {
                    isPowerOfTwo = false,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                DUMMY_HOLES_TEXTURE.Create();
                Texture2D dummy2D = new Texture2D(1, 1);
                dummy2D.SetPixel(0, 0, Color.white);
                GPUITextureUtility.CopyTextureSamplerWithComputeShader(dummy2D, DUMMY_HOLES_TEXTURE);
                dummy2D.DestroyGeneric();
            }
            return DUMMY_HOLES_TEXTURE;
        }

        public virtual int GetAlphamapTextureCount()
        {
            return 0;
        }

        public virtual Texture2D[] GetAlphamapTextures()
        {
            return null;
        }

        public virtual TerrainLayer[] GetTerrainLayers()
        {
            return null;
        }

        public virtual bool HasScalingSupport() => false;

        public virtual bool HasRotationSupport() => false;

        public virtual Vector3 GetTerrainScale() => Vector3.one;

        public virtual Quaternion GetTerrainRotation() => GPUIConstants.IDENTITY_Quaternion;

        internal Matrix4x4 GetRotationMatrix() => _cachedRotationMatrix;

        public virtual Quaternion GetSavedTerrainRotation() => GPUIConstants.IDENTITY_Quaternion;

        public virtual bool HasTwoChannelHeightmap() => !GPUIRuntimeSettings.Instance.API_HAS_GUARANTEED_R8_SUPPORT;

        public void SetProceduralDetailModifier(IGPUIProceduralDetailModifier detailModifier)
        {
            _proceduralDetailModifier = detailModifier;
            RequireDetailUpdate(false, true);
        }

        protected bool IsReadTerrainDetails(int terrainDetailPrototypeIndex)
        {
            if (_proceduralDetailModifier != null && !_proceduralDetailModifier.IsReadTerrainDetails(terrainDetailPrototypeIndex))
                return false;
            if (DetailManager != null && DetailPrototypeIndexes != null && DetailPrototypeIndexes.Length > terrainDetailPrototypeIndex && !DetailManager.IsReadTerrainDetails(DetailPrototypeIndexes[terrainDetailPrototypeIndex] % GPUIDetailManager.DETAIL_SUB_SETTING_DIVIDER))
                return false;
            return true;
        }

        #endregion Getters / Setters

        #region Editor Methods
#if UNITY_EDITOR
        protected void OnDrawTerrainGizmos()
        {
            if (_cachedTransform == null) return;

            Color gizmoDefaultColor = Gizmos.color;
            Bounds worldBounds = GetTerrainWorldBounds();
            // Draw World Bounds
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);

            // Draw Bottom Left Position
#if GPUIPRO_DEVMODE
            Gizmos.color = Color.red;
            Gizmos.DrawCube(GetPosition(), Vector3.one);
#endif

            // Draw Rotated Terrain Bounds
            Gizmos.color = Color.cyan;
            Matrix4x4 offset = GPUIConstants.IDENTITY_Matrix4x4;
            if (HasRotationSupport())
                offset = GPUIUtility.GetRotationMatrix(GetTerrainRotation());
            offset.SetPosition(worldBounds.center);
            Gizmos.matrix = offset;
            Gizmos.DrawWireCube(Vector3.zero, GetSize());
            Gizmos.matrix = GPUIConstants.IDENTITY_Matrix4x4;

            Gizmos.color = gizmoDefaultColor;
        }
#endif
        #endregion Editor Methods

        public enum GPUITerrainHolesSampleMode
        {
            /// <summary>
            /// Only sample terrain holes texture during initialization
            /// </summary>
            Initialization = 0,
            /// <summary>
            /// Sample terrain holes texture every time the detail instances are regenerated
            /// </summary>
            Runtime = 1,
            /// <summary>
            /// Never sample terrain holes texture
            /// </summary>
            None = 2,
        }

        public abstract class GPUITerrainPaintingProxy : MonoBehaviour
        {
            public abstract void FinalizePainting(bool saveTerrainData);
        }
    }

    public interface IGPUIProceduralDetailModifier
    {
        bool IsReadTerrainDetails(int terrainDetailPrototypeIndex);
        void Execute(GPUITerrain gpuiTerrain);
    }
}