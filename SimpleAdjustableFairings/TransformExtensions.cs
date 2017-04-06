using UnityEngine;

namespace SimpleAdjustableFairings
{
    public static class TransformExtensions
    {
        public static void MakeTransparent(this Transform transform, float opacity = 0.5f)
        {
            foreach (MeshRenderer meshRenderer in transform.GetComponentsInChildren<MeshRenderer>())
            {
                meshRenderer.material.renderQueue = 6000;
                meshRenderer.material.SetFloat(PropertyIDs._Opacity, opacity);
            }
        }

        public static void MakeOpaque(this Transform transform)
        {
            foreach (MeshRenderer meshRenderer in transform.GetComponentsInChildren<MeshRenderer>())
            {
                meshRenderer.material.renderQueue = -1;
                meshRenderer.material.SetFloat(PropertyIDs._Opacity, 1f);
            }
        }

        public static void SetCollidersEnabled(this Transform transform, bool enabled)
        {
            foreach (Collider collider in transform.GetComponentsInChildren<Collider>())
            {
                collider.enabled = enabled;
            }
        }
    }
}
