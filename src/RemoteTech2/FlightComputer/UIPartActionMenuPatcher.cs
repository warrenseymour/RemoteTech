using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RemoteTech
{
    public static class UIPartActionMenuPatcher
    {
        public static void Wrap(Vessel parent, Action<BaseEvent, bool> pass)
        {
            var controller = UIPartActionController.Instance;
            if (!controller) return;
            var listFieldInfo = controller.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(fi => fi.FieldType == typeof(List<UIPartActionWindow>));

            var list = (List<UIPartActionWindow>)listFieldInfo.GetValue(controller);
            foreach (var window in list.Where(l => l.part.vessel == parent))
            {
                var itemsFieldInfo = window.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .First(fi => fi.FieldType == typeof(List<UIPartActionItem>));

                var item = (List<UIPartActionItem>)itemsFieldInfo.GetValue(window);
                foreach (var it in item)
                {
                    var button = it as UIPartActionEventItem;
                    if (button != null)
                    {
                        var partEventFieldInfo = button.Evt.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                            .First(fi => fi.FieldType == typeof(BaseEventDelegate));

                        var partEvent = (BaseEventDelegate)partEventFieldInfo.GetValue(button.Evt);
                        if (!partEvent.Method.GetCustomAttributes(typeof(KSPEvent), true).Any(a => ((KSPEvent)a).category.Contains("skip_control")))
                        {
                            bool ignoreDelay = partEvent.Method.GetCustomAttributes(typeof(KSPEvent), true).Any(a => ((KSPEvent)a).category.Contains("skip_delay"));
                            var eventField = typeof(UIPartActionEventItem).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                                .First(fi => fi.FieldType == typeof(BaseEvent));
                            eventField.SetValue(button, Wrapper.CreateWrapper(button.Evt, pass, ignoreDelay));
                        }
                    }
                }
            }
        }

        private class Wrapper
        {
            private readonly Action<BaseEvent, bool> passthrough;
            private readonly BaseEvent baseEvent;
            private readonly bool ignoreDelay;

            private Wrapper(BaseEvent original, Action<BaseEvent, bool> passthrough, bool ignoreDelay)
            {
                this.passthrough = passthrough;
                baseEvent = original;
                this.ignoreDelay = ignoreDelay;
            }

            public static BaseEvent CreateWrapper(BaseEvent original, Action<BaseEvent, bool> passthrough, bool ignoreDelay)
            {
                var cn = new ConfigNode();
                original.OnSave(cn);
                var wrapper = new Wrapper(original, passthrough, ignoreDelay);
                var newEvent = new BaseEvent(original.listParent, original.name, wrapper.Invoke);
                newEvent.OnLoad(cn);

                return newEvent;
            }

            [KSPEvent(category="skip_control")]
            public void Invoke()
            {
                passthrough.Invoke(baseEvent, ignoreDelay);
            }
        }
    }
}