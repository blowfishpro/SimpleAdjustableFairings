using UnityEngine;

namespace SimpleAdjustableFairings
{
    public class ModelData
    {
        [Persistent]
        public string name;

        [Persistent]
        public string transformName;

        [Persistent]
        public float mass;

        [Persistent]
        public Vector3 CoM;

        [Persistent]
        public Vector3 rootOffset;
    }
}
