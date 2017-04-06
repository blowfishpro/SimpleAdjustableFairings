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

                IPartCoMModifier modifier2 = modifier as IPartCoMModifier;
                if (modifier2 != null)
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

        public static void LogInfo(this Part part, object message) => Debug.Log($"[{part.name}] {message}");
        public static void LogWarning(this Part part, object message) => Debug.LogWarning($"[{part.name}] {message}");
        public static void LogError(this Part part, object message) => Debug.LogError($"[{part.name}] {message}");

        public static void LogInfo(this PartModule module, object message) => Debug.Log($"[{module.part.name} {module.ClassName}] {message}");
        public static void LogWarning(this PartModule module, object message) => Debug.LogWarning($"[{module.part.name} {module.ClassName}] {message}");
        public static void LogError(this PartModule module, object message) => Debug.LogError($"[{module.part.name} {module.ClassName}] {message}");
    }
}
