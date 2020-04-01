using UnityEngine;

namespace SimpleAdjustableFairings
{
    public static class GameObjectExtensions
    {
        public static void MakeTransparent(this GameObject gameObject, float opacity = 0.5f)
        {
            foreach (MeshRenderer meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>(true))
            {
                meshRenderer.material.renderQueue = 6000;
                meshRenderer.material.SetFloat(PropertyIDs._Opacity, opacity);
            }
        }

        public static void MakeOpaque(this GameObject gameObject)
        {
            foreach (MeshRenderer meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>(true))
            {
                meshRenderer.material.renderQueue = -1;
                meshRenderer.material.SetFloat(PropertyIDs._Opacity, 1f);
            }
        }

        public static void SetCollidersEnabled(this GameObject gameObject, bool enabled)
        {
            foreach (Collider collider in gameObject.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = enabled;
            }
        }
    }
}
