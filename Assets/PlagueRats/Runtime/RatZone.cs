using System.Collections.Generic;
using UnityEngine;

namespace PlagueRats
{
    /// <summary>
    /// A box volume that holds rats. Place these in your dungeon wherever you want
    /// rats to carpet the floor. Each zone spawns its own rats and keeps them inside.
    /// No grid, no flow field — just a box.
    /// </summary>
    public class RatZone : MonoBehaviour
    {
        [Tooltip("How many rats live in this zone.")]
        public int ratCount = 300;

        [Tooltip("Box size in world units (XZ used; Y ignored for movement).")]
        public Vector3 size = new Vector3(10f, 1f, 10f);

        public static readonly List<RatZone> All = new();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>World-space min/max on the XZ plane.</summary>
        public Vector2 MinXZ => new Vector2(transform.position.x - size.x * 0.5f,
                                            transform.position.z - size.z * 0.5f);
        public Vector2 MaxXZ => new Vector2(transform.position.x + size.x * 0.5f,
                                            transform.position.z + size.z * 0.5f);
        public float GroundY => transform.position.y;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 0.3f, 0.25f);
            Gizmos.DrawCube(transform.position, new Vector3(size.x, 0.1f, size.z));
            Gizmos.color = new Color(0.4f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(transform.position, new Vector3(size.x, 0.1f, size.z));
        }
    }
}