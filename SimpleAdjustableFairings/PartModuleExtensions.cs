namespace SimpleAdjustableFairings
{
    public static class PartModuleExtensions
    {
        public static UI_Control GetUIControl(this PartModule module, string name)
        {
            BaseField field = module.Fields[name];

            if (field == null) return null;

            return HighLogic.LoadedSceneIsEditor ? field.uiControlEditor : field.uiControlFlight;
        }

        public static T GetUIControl<T>(this PartModule module, string name) where T : UI_Control
        {
            return (T)module.GetUIControl(name);
        }
    }
}
