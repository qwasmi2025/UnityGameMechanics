# Changelog
All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/) and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.12.16] - 2026-05-24

### Changed
- Improved performance for the Tree Manager when loading tree instance data.
- Improved GC performance for the Tree Instance Colors feature in the Tree Manager.
- Added an early-out check to the SetDetailDensityTexture method to ignore updates when assigning the same render texture.
- SetDetailDensityTexture now releases the previous render texture only if it was created by the Detail Manager, preventing accidental release of user-created textures.

### Fixed
- Removed obsolete RenderGraphSettings checks for Unity 6.4.
- Fixed obsolete material import warnings for models in Unity 6.4.
- Fixed an IndexOutOfRangeException that could occur when a prototype reference was null while a new prototype was being added at the same time.

## [0.12.15] - 2026-04-06

### Fixed
- Fixed obsolete method warnings in Unity 6.4.
- Resolved an issue where trees using SpeedTree shaders did not render when the LOD_FADE_PERCENTAGE keyword was enabled.

## [0.12.14] - 2026-02-27

### Changed
- The Built-in Shaders package is now automatically imported when a Built-in shader is used and the corresponding GPUI Pro shader is missing, either because it was deleted or due to a render pipeline change.

### Fixed
- Fixed a NullReferenceException that occurred when adding prototypes to a new Tree Manager via code.

## [0.12.13] - 2026-02-22

### Fixed
- Fixed a shader conversion issue affecting surface shaders in the Built-in Render Pipeline.

## [0.12.12] - 2026-02-17

### New
- Improved Tree Manager terrain processing performance when working with a large number of terrain chunks.
- Added an Update Baked Textures button to the Detail Manager to easily save changes to all baked detail textures after modifications.
- Added ResetMaterials method to CoreAPI.
- Added a warning in Prefab Manager when a prototype uses a Skinned Mesh Renderer but the Crowd Animations package is not installed.

### Fixed
- Fixed a rendering issue that occurred when multiple RenderSources were rendering the same prototype and some of them had a zero buffer size.
- Fixed the OnPrototypeEnabledStatusChanged method not functioning when the GPUI Manager was not yet initialized.
- Fixed a shader conversion error for Crowd Animations on HDRP.

## [0.12.11] - 2026-01-11

### Fixed
- Fixed an issue where the Detail Manager caused frequent Scene View updates in edit mode, reducing edit mode rendering performance.

## [0.12.10] - 2026-01-10

### Changed
- Prefab prototypes now support an unlimited number of Optional Renderers.
- Added XR camera checks when determining culling mode to support setups with multiple cameras, both XR-enabled and non-XR.

### Fixed
- Fixed an issue where shaders occasionally used incorrect instance data buffer indices when using Optional Renderers.

## [0.12.9] - 2026-01-09

### Changed
- Added position input and output to the tree proxy vertex shader for improved cross-platform compatibility.
- Added extra index checks when removing a prefab instance to prevent potential out-of-bounds exceptions.

### Fixed
- Fixed a benign Serializable warning related to CustomPass in Unity 6.3 HDRP.
- Fixed potentially uninitialized variable (transformOffset) build warning in Unity 6.3.
- Fixed the AddPrefabInstanceImmediate method to use the instance count instead of the buffer size when assigning bufferIndex.
- Fixed "Ignoring duplicate keyword line" shader warnings for SpeedTree shaders.
- Fixed a bounding box calculation error when a child renderer has a positional and rotational offset.
- Fixed a memory leak when regenerating renderers while using Optional Renderers.
- Minor UI fixes.

## [0.12.8] - 2025-12-12

### New
- Billboard settings now show custom texture and color property selection when the shaders does not use one of the predefined property names.
- Optimizations for the terrain system when using large numbers of terrain chunks.
- Added an option to disable terrain bounds checks for the Detail Manager.
- Added an option to toggle distance-based detail density reduction per terrain.
- UI improvements.

### Fixed
- Fixed a benign edit-mode error caused by the Camera.Render call when changing detail prototype settings in the latest SRP versions.
- When the billboard shader cannot be found, the system now logs an error instead of throwing repeating exceptions.

## [0.12.7] - 2025-11-27

### New
- Added UNITY_SERVER check to automatically disable GPU Instancer on server builds.

### Fixed
- In URP and HDRP demo scenes, fixed the “Camera does not contain an additional camera data component.” warning in Unity 6.2 or higher.
- Fixed a NullReferenceException in PrefabManager when registeredInstances is null.

## [0.12.6] - 2025-11-24

### New
- Added the Auto Enable by Instance Count feature for Prefab Manager prototypes.
- Added new validation checks to prevent container prefabs from being added as prototypes.
- UI improvements.

## [0.12.5] - 2025-11-24

### New
- Improved performance by reducing the number of compute shader dispatch calls when using multiple render sources.
- Improved detail and tree instance update performance during terrain movement.
- Reduced GC allocations during Tree Manager matrix updates.
- Added context menu options to GPUITerrain for adding prototypes to terrain data from Tree and Detail Managers.

### Fixed
- Resolved the obsolete TryGetGUIDAndLocalFileIdentifier warning in Unity 6.3.
- Fixed a GUI error when adding Texture prototypes to Detail Manager in Unity 6.2.
- Resolved an issue where adding a prototype to Detail Manager and selecting “Add to Terrains” caused the prototype to duplicate.
- Fixed a flickering issue in XR when GPUI Camera initialized before the XR device while using Single Pass Instanced.

## [0.12.4] - 2025-11-01

### Fixed
- Fixed a syntax error in the shader converter.

## [0.12.3] - 2025-10-30

### New
- Added a "Fix Terrain List" button to the Tree and Detail Managers for easily removing null or duplicate terrain references.

### Changed
- The GPUITerrainBuiltin component no longer initializes the rendering system on its own unless registered with a Detail or Tree Manager.
- Commonly used GPUI components are now automatically disabled on unsupported platforms.
- Added a preferences option to include a script define symbol that disables error messages on unsupported platforms.
- Improved Built-in RP surface shader conversion for better compatibility.

### Fixed
- Resolved an issue where Material Variation values were transferred to another instance when removing a prefab instance.
- Resolved an issue where Material Variations were disabled after calling the Regenerate Renderers method.

## [0.12.2] - 2025-10-01

### New
- Added an Edit button next to the Prefab Object field in the Detail Manager, allowing you to replace the prefab detail prototype on the terrains with another prefab (also supports LOD Groups).

### Fixed
- Fixed rendering issues caused by incorrect Instancing Bounds calculation on Vulkan and OpenGL.
- Resolved an issue where disabling the Prefab Manager in the same frame as removing instances, and then re-enabling it, caused errors.

## [0.12.1] - 2025-09-05

### Changed
- Added advanced runtime settings to control buffer usage and the maximum compute work group size.
- Added additional filters and optimized the Prefab Selector editor window for improved speed and reduced memory usage.

### Fixed
- Resolved an issue where the Prefab Manager's RequireTransformUpdate method did not apply updates.

## [0.12.0] - 2025-08-25

### New
- Optimized SetPass calls for better performance.
- Added Calculate Instancing Bounds option for async bounds calculations and reduced batch counts.
- Added Override Shadow Layer option to Profile settings, allowing different layers to be used for shadow draw calls.
- Improved Prefab Manager performance for transform updates.
- Added support for Mesh LOD Override on Mesh Renderer components in Unity 6.2. (Mesh LOD is not fully supported, but a specific LOD can be selected for all instances.)
- Added Force Mesh LOD option to Profile settings, allowing a specific mesh LOD to be used if the mesh has LODs in Unity 6.2.

### Changed
- System now determines operation mode based on hardware capabilities instead of Graphics API or platform.
- Added a multi_compile keyword (GPUI_NO_BUFFER) to shaders for devices with limited hardware. Shader variants using this keyword are automatically stripped from PC and modern console builds.

### Fixed
- Resolved rendering issues on Android devices with Xclipse GPUs using the Vulkan API.
- Fixed flickering issue when using Optional Renderers.

## [0.11.3] - 2025-08-12

### New
- Improved shader generation for Prefab Material Variations.
- Added Crowd Animations support to the Variation Definition.
- Implemented wind simulation for Tree Creator shaders with per-instance variation.
- Added the RequireTransformUpdate method to the Prefab Manager as an alternative to the Transform Updates option.
- Added an Auto Generate Billboards option to the Tree Manager to make billboard generation optional.

### Fixed
- Resolved an issue where tree wind animations would not work with the Prefab Manager if the original prefab’s Mesh Renderers were disabled.
- Fixed incorrect wind direction for rotated Tree Creator trees.
- Fixed a benign editor error that occurred when removing a prototype from the Prefab Manager.

## [0.11.2] - 2025-07-24

### Fixed
- Resolved rendering issue on Linux with Vulkan API.
- Fixed editor error caused by preview cache during assembly reload.

## [0.11.1] - 2025-07-23

### Changed
- Added a dialog box with an Overwrite option when running shader setup from the menu.

### Fixed
- Resolved shader conversion issue with the Better Lit Shader.
- Added missing linear-to-gamma conversion for tree instance colors.

## [0.11.0] - 2025-06-20

### New
- Performance improvement by reducing shadow draw calls when using the "Show LOD Map" setting.
- Improved performance for vegetation transform matrix calculations.
- Added an Ignore Renderers component which, when added to a child GameObject of a prefab, prevents GPUI from rendering the renderers on that GameObject and its children, and ensures their Renderer components are not disabled by the Prefab Manager.

### Fixed
- Eliminated a GC allocation caused by accessing Component.tag when using multiple cameras.
- Fixed an issue where the Terrain module attempted to load the Foliage shader before it was imported.
- Minor UI fixes.

## [0.10.3] - 2025-05-24

### New
- Added an Amplify Shader Function for easily integrating GPU Instancer Pro setup into ASE shaders.
- Added support for the All In 1 3D Shader asset.

### Changed
- Improved Shader Converter for better compatibility with third-party assets.
- Removed the shader files from the Packages. Shaders specific to the render pipeline will now be imported into the Assets/GPUInstancerPro/Shaders folder.

### Fixed
- Fixed an issue where the UNITY_DEFINE_INSTANCED_PROP macro prevented the shader variables from being set.

## [0.10.2] - 2025-05-12

### New
- Shaders now have new local shader features for Per Object Motion Vectors and Per Instance Light Probes, which are automatically enabled only when necessary.
- Added new editor settings to disable Per Object Motion Vectors or Per Instance Light Probes and strip shader variants with their keywords from builds.
- Added new editor setting to strip non-procedural instancing variants of GPUInstancerPro shaders in builds.

### Changed
- Various shader keywords that were used with multi_compile have been changed to shader_feature_local for improved efficiency.
- Added a prefix to all log messages to clearly indicate they are generated by GPU Instancer Pro.

### Fixed
- Fixed an error in Unity 6: "Attempting to draw with missing bindings."
- Fixed a warning in Unity 6: "Adding null Transform to TransformAccessArray will result in degraded performance."
- Fixed URP Lit shader warning in Unity 6.1: "Ignoring duplicate keyword line multi_compile_instancing"

## [0.10.1] - 2025-05-10

### Fixed
- Fixed a null reference error that occurred when a Light Probe update was triggered before the render keys were initialized.

## [0.10.0] - 2025-05-09

### New
- Added Light Probe support.
- New demo scene showcasing Light Probes usage at Demos/Core/TutorialScenes/NoGameObjectLightProbes.

### Changed
- Reduced the amount of data sent to GPU when using Transform Updates on Prefab Manager.

## [0.9.19] - 2025-05-03

### New
- The Prefab Manager now includes an Optional Renderers section, allowing users to designate specific child renderers as optional and control their enable/disable state at runtime.
- Added SetDetailDensityAdjustment and SetTreeInstances GPUITerrainAPI methods.
- Per Object Motion Vectors can now be enabled for URP as well as HDRP.

### Changed
- Replaced all relative shader paths with full paths for improved compatibility and consistency.
- Reduced GC allocations when setting Material Variation values at runtime.
- Rendering System Camera events have been changed to use Action instead of UnityEvent for better performance and reduced allocations.
- Removed LOD_FADE_CROSSFADE keyword check while setting unity_LODFade to allow for custom cross-fading implementations.

### Fixed
- Resolved an issue where SpeedTree material properties were not applied when the prefab lacked a Tree component.
- Fixed an editor memory leak related to GPUILODGroupData.
- Fixed SpeedTree8 shader warnings and added SpeedTree9 shader support for Unity 6000.1.

## [0.9.18] - 2025-02-17

### Fixed
- Resolved the terrain alignment issue for detail instances.
- Fixed an issue where the GPUIProPackageImportedData file was still being created at the default path after moving the GPUInstancerPro folder.

## [0.9.17] - 2025-02-11

### New
- Added edit mode rendering capability to the Prefab Manager when default renderers on the prefabs are disabled.
- Added new debugging methods for giving different colors to each LOD. Also can be accessed from the Statistics tab or the GPUI Debugger Window by clicking on the "..." button on a render group.
- Improved AddMaterialPropertyOverride functionality and added RemoveMaterialPropertyOverrides and ClearMaterialPropertyOverrides API methods.
- Added a warning message and a fix button for prefab terrain references in the Terrains list of Detail Manager and Tree Manager.

### Changed
- In HDRP, the Enable Motion Vectors profile setting has been renamed to Enable Per Object Motion for clarity.
- Optimized automatic Add/Remove operations for the Prefab Manager during initialization, improving performance.
- UI improvements.

### Fixed
- Fixed an issue where the material variation definition could not be created if the prefab's parent had no renderers.
- Fixed a null reference error that occurred when terrain tree prototypes could not be found.
- Fixed the NoGameObjectUpdatesCompute demo script to function correctly on Android devices.
- Resolved a benign compiler warning in GPUICameraVisibilityCS on Android devices.

## [0.9.16] - 2025-01-23

### New
- Cross-Fading now supports assigning different Fade Transition Width values for each LOD level.
- Added an error log for the player when the shader with the GPUI Pro Setup cannot be found or was not included in the build.

### Changed
- The LOD Cross Fade Transition Width profile setting is now obsolete. Transition width values are now taken directly from the LOD Group component.
- Optimized the Auto-Find processes in the Tree and Detail Managers for terrains, enhancing terrain streaming performance.

## [0.9.15] - 2025-01-15

### New
- Added the isOverwritePreviousFrameBuffer parameter to the SetTransformBufferData API methods to support Motion Vectors.

### Changed
- The GPUICamera component is now automatically added to cameras with the MainCamera tag when additional cameras are loaded at runtime.

### Fixed
- Resolved an error that occurred in Unity 6 with SpeedTree8 shaders.
- Fixed a compilation error that occurred when both URP and HDRP packages were installed.
- Addressed an issue where the Occlusion Culling depth texture did not update in the Built-in Deferred rendering path.
- Fixed a culling issue when the camera was inside the bounding box of an instance.
- Corrected a rounding error in depth texture mip level calculations when using dynamic scaling.
- Resolved the issue of GPUI instances flashing in the Unity editor when the Game window is redrawn (e.g., moved, resized, etc.).
- Resolved an issue where the Scene view camera did not render instances when it was far from the game camera.

## [0.9.14] - 2024-12-13

### New
- Added GPUI Area Culler component to cull user-specified areas based on colliders or bounds.
- Added a demo scene showcasing the usage of the GPUI Area Culler.
- Added toolbar menu items to set up selected material shaders for GPUI Pro and replace material shaders with GPUI Pro variants.

### Changed
- GPUITransformBufferUtility RemoveInstances* methods are now obsolete and have been replaced with CullInstances* methods for improved functionality and clarity.

### Fixed
- Performance improvements and fixes for GPUIManager UI events.
- Fixed an issue where modifying the mesh or materials of a texture-type detail prototype unintentionally impacted other texture-type detail prototypes.
- Resolved a crash that occurred when adding nested prefabs to the Prefab Manager.
- Resolved Debugger Canvas scaling issue.
- Resolved an issue with detail instance positioning at the (0,0) index on the terrain.

## [0.9.13] - 2024-11-19

### New
- Added HDRP Dynamic Resolution support for occlusion culling.
- Introduced a new Profile setting, Occlusion Offset Size Multiplier, for adjusting the expansion of the bounding box used in occlusion culling.
- Introduced a debug button in the scene view overlay to inspect the depth texture used for occlusion culling (visible only when the camera is selected).

### Fixed
- Resolved an error occurring when Render Graph Compatibility Mode was enabled in Unity 6 URP.
- Corrected the OptimizedShader dependency for the 'Tree Creator Leaves' shader.
- Fixed and optimized XR occlusion culling.
- Fixed an issue where detail density render textures would initially load random data from GPU memory.
- Resolved an issue where prefab material variations failed to locate the variation material.
- Detail instances now render correctly after removing all terrains and re-adding them to the Detail Manager at runtime.
- Detail density modifier now checks for terrain heightmap type for platform compatibility.

## [0.9.12] - 2024-11-07

### Changed
- Implemented several quality-of-life improvements to the user interface.

### Fixed
- Resolved incorrect bounds calculation for billboards when the original prefab has a scale other than 1.
- Fixed issue where child transforms were not removed when creating Tree Proxy GameObjects.
- Fixed a memory leak related to the mesh and material created for billboards.

## [0.9.11] - 2024-11-04

### Changed
- Detail density calculation improvements for Coverage Mode.
- Refined terrain module design to support better extensibility.
- Optimized data loading performance for the Tree Manager.

### Fixed
- Resolved an issue where the incorrect tree prototype was rendered when multiple tree prototypes shared the same prefab.
- Resolved an issue where billboard textures were empty when a tree prototype was initially added from the terrain to the Tree Manager.
- Resolved a UI error that occurred when a prototype was automatically removed from the manager.

## [0.9.10] - 2024-10-29

### New
- Added a Scene View overlay to chose between different rendering modes for the Scene camera. The Scene View camera now has the option the make its own visibility calculations at runtime. Allowing users to see the objects that are culled by the Game camera.
- Added a Runtime Settings option to select the Depth texture retrieval method for the Occlusion Culling system.
- The rendering system now respects the Maximum LOD Level quality setting and avoids rendering LODs higher than the specified level. Additionally users can set a Maximum LOD Level through Profile settings to have different settings for diffferent prototypes.
- Added an editor setting to prevent Unity from including shader variants with both DOTS instancing and procedural instancing keywords in builds.

### Changed
- Redesigned the Occlusion Culling system for improved compatibility with future Unity changes.
- Added various quality-of-life improvements to the user interface.

### Fixed
- Resolved Occlusion Culling issues in Unity 6000 URP and HDRP.

## [0.9.9] - 2024-10-23

### Fixed
- Tree Proxy shader not automatically included in builds, causing errors with SpeedTrees.

## [0.9.8] - 2024-10-22

### Fixed
- Compile error caused by Input System reference in Unity 6000.0.23f1.
- Obsolete HDRP light intensity warning in Unity 6000.0.23f1.
- Shader warning in the Material Variation Demo Scene in Unity 6000.0.23f1.
- Shader conversion error in Material Variation when using built-in shaders.

## [0.9.7] - 2024-10-03

### Added
- New demo showcasing how to use custom Compute Shaders.
- New API method to retrieve the GraphicsBuffer containing the Matrix4x4 transform data.

### Changed
- The Material Variations shader generator now uses relative paths for include files.
- Improved TransformBufferUtility methods to support multiple RenderSources using the same transform buffer.

### Fixed
- Prefabs with an LOD Group that has only one level and a culled percentage not being culled.
- Prefabs with an LOD Group not cross-fading to the culled level when using a culled percentage.
- Compute shader error that occurred when there were multiple RenderSources within a RenderSourceGroup and one RenderSource had a zero instance count.

## [0.9.6] - 2024-09-10

### Fixed
- Resolved rendering issues on devices with AMD GPUs.

## [0.9.5] - 2024-09-09

### Added
- Prefab Manager Add/Remove instance performance improvements.
- In edit mode, the Tree and Detail Managers can now render terrain details and trees from other scenes.
- The Tree and Detail Managers now include an option to automatically add terrains from scenes loaded at runtime.
- Map Magic 2 integration component for runtime generated terrains.
- UI improvements for GPUI Managers.

### Changed
- Camera FOV value is no longer cached and is now updated automatically.
- Auto. Find Tree and Detail Manager options for GPUI Terrain is now enabled by default.

### Fixed
- Prototype could not be removed from the Prefab Manager if the GPUIPrefab component was manually deleted from a prefab.
- GPUIPrefab component was not automatically added to a prefab when using a variant of a model prefab as a prototype on the Prefab Manager.
- 'Add Active Terrains' button on Detail and Tree Managers would add references to terrains in other scenes when multiple scenes with terrains were loaded in edit mode, causing a 'Scene mismatch' error.

## [0.9.4] - 2024-08-10

### Added
- New RequireUpdate API methods for Tree and Detail Managers to handle runtime terrain modifications.

### Fixed
- Detail Manager IndexOutOfRangeException when using Coverage mode with multiple prototypes that have the same prefab or texture.

## [0.9.3] - 2024-07-24

### Fixed
- Managers not showing the correct package version number.

## [0.9.2] - 2024-07-24

### Fixed
- Wrong render pipeline is selected for rendering and importing demos when the Render Pipeline Asset in Quality settings is not set.

## [0.9.1] - 2024-07-23

### Fixed
- New Profile objects are not editable.
- Removed unused using statements.

## [0.9.0] - 2024-07-22

### Added
- Initial release.