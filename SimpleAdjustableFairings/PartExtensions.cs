using System;
using UnityEngine;

namespace SimpleAdjustableFairings
{
    public interface IPartCoMModifier : IPartMassModifier
    {
        Vector3 GetModuleCoM();
    }

    public static class PartExtensions
    {
        public static void ModifyCoM(this Part part)
        {
            float prefabMass = part.partInfo.partPrefab.mass;
            float mass = prefabMass + part.GetResourceMass();
            Vector3 prefabCoM = part.partInfo.partPrefab.CoMOffset;
            Vector3 CoM = prefabCoM * mass;

            foreach (IPartMassModifier modifier in part.FindModulesImplementing<IPartMassModifier>())
            {
                float moduleMass = modifier.GetModuleMass(prefabMass, ModifierStagingSituation.CURRENT);
                mass += moduleMass;

                if (modifier is IPartCoMModifier modifier2)
                    CoM += modifier2.GetModuleCoM() * moduleMass;
                else
                    CoM += prefabCoM * moduleMass;
            }

            CoM /= mass;

            part.CoMOffset = CoM;

#if DEBUG
            part.LogInfo($"Calcualted CoM: {CoM.x :F2}, {CoM.y:F2}, {CoM.z:F2}");
#endif
        }

        public static void LogInfo(this Part part, object message) => Debug.Log($"[{SafePartName(part)}] {message}");
        public static void LogWarning(this Part part, object message) => Debug.LogWarning($"[{SafePartName(part)}] {message}");
        public static void LogError(this Part part, object message) => Debug.LogError($"[{SafePartName(part)}] {message}");

        public static void LogInfo(this PartModule module, object message) => Debug.Log($"[{SafeModuleTag(module)}] {message}");
        public static void LogWarning(this PartModule module, object message) => Debug.LogWarning($"[{SafeModuleTag(module)}] {message}");
        public static void LogError(this PartModule module, object message) => Debug.LogError($"[{SafeModuleTag(module)}] {message}");

        public static void LogException(this PartModule module, Exception exception) => Debug.LogException(new System.Exception($"Exception on {SafeModuleTag(module)}", exception));

        private static string SafePartName(Part part) => part?.partInfo?.name ?? "<unknown part>";
        private static string SafeModuleTag(PartModule module) => SafePartName(module?.part) + ' ' + (module?.GetType().Name ?? "<null module>");
    }
}
