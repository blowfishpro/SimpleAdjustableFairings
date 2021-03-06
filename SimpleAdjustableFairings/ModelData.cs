using System;
using UnityEngine;

namespace SimpleAdjustableFairings
{
    public class ModelData
    {
        public abstract class ResolveException : Exception
        {
            public ResolveException(string message) : base(message) { }
        };

        public class TransformNameMissingException : ResolveException
        {
            public TransformNameMissingException() : base("Transform name is null or empty") { }
        }

        public class ObjectNotFoundException : ResolveException
        {
            public ObjectNotFoundException(string name) : base($"No object named {name} could be found") { }
        }

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

        [Persistent]
        public bool enabled = true;

        public ResolvedModelData Resolve(GameObject lookupRoot, float scale)
        {
            if (!enabled) return null;

            if (lookupRoot == null) throw new ArgumentNullException(nameof(lookupRoot));

            if (string.IsNullOrEmpty(transformName)) throw new TransformNameMissingException();
            
            GameObject prefab = lookupRoot.GetChild(transformName);

            if (prefab == null) throw new ObjectNotFoundException(transformName);

            return new ResolvedModelData(prefab, mass * scale * scale, CoM * scale, rootOffset * scale);
        }
    }

    public class ResolvedModelData
    {
        public readonly GameObject gameObject;
        public readonly float mass;
        public readonly Vector3 CoM;
        public readonly Vector3 rootOffset;

        public ResolvedModelData(GameObject gameObject, float mass, Vector3 CoM, Vector3 rootOffset)
        {
            this.gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
            this.mass = mass;
            this.CoM = CoM;
            this.rootOffset = rootOffset;
        }
    }
}
