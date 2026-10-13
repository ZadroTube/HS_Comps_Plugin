using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using Hearthstone_Deck_Tracker.API;

namespace HSCompsPlugin.Services
{
    /// <summary>
    /// Обеспечивает интеграцию элементов оверлея плагина с подсистемой ввода HDT (OverlayWindow).
    /// HDT использует стиль WS_EX_TRANSPARENT (_clickthrough = true) для окна оверлея,
    /// пропуская клики в Hearthstone, пока курсор не оказывается над элементом из _clickableElements / _hoverableElements.
    /// Регистрация элементов в этих коллекциях включает полноценный ввод мыши в WPF (клики, ховеры, перетаскивание, тултипы).
    /// </summary>
    public static class HdtOverlayInputManager
    {
        private static FieldInfo _clickableElementsField;
        private static FieldInfo _hoverableElementsField;
        private static MethodInfo _setClickthroughMethod;
        private static bool _initialized = false;

        private static void EnsureInitialized()
        {
            if (_initialized && _clickableElementsField != null) return;

            try
            {
                var overlayWindow = Core.OverlayWindow;
                if (overlayWindow == null) return;

                var type = overlayWindow.GetType();
                _clickableElementsField = type.GetField("_clickableElements", BindingFlags.NonPublic | BindingFlags.Instance);
                _hoverableElementsField = type.GetField("_hoverableElements", BindingFlags.NonPublic | BindingFlags.Instance);

                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    if (m.Name == "SetClickthrough" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool))
                    {
                        _setClickthroughMethod = m;
                        break;
                    }
                }

                if (_clickableElementsField != null)
                {
                    _initialized = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdtOverlayInputManager] Ошибка инициализации рефлексии HDT: {ex.Message}");
            }
        }

        public static void SetClickthrough(bool clickthrough)
        {
            try
            {
                EnsureInitialized();
                var overlayWindow = Core.OverlayWindow;
                if (overlayWindow == null) return;

                if (overlayWindow.Dispatcher != null && !overlayWindow.Dispatcher.CheckAccess())
                {
                    overlayWindow.Dispatcher.Invoke(new Action(() => SetClickthrough(clickthrough)));
                    return;
                }

                if (_setClickthroughMethod != null)
                {
                    _setClickthroughMethod.Invoke(overlayWindow, new object[] { clickthrough });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdtOverlayInputManager] Ошибка SetClickthrough: {ex.Message}");
            }
        }

        public static void RegisterElement(FrameworkElement element)
        {
            if (element == null) return;

            try
            {
                EnsureInitialized();
                var overlayWindow = Core.OverlayWindow;
                if (overlayWindow == null) return;

                if (overlayWindow.Dispatcher != null && !overlayWindow.Dispatcher.CheckAccess())
                {
                    overlayWindow.Dispatcher.Invoke(new Action(() => RegisterElement(element)));
                    return;
                }

                if (_clickableElementsField != null)
                {
                    if (_clickableElementsField.GetValue(overlayWindow) is IList clickableList)
                    {
                        if (!clickableList.Contains(element))
                        {
                            clickableList.Add(element);
                        }
                    }
                }

                if (_hoverableElementsField != null)
                {
                    var hoverableObj = _hoverableElementsField.GetValue(overlayWindow);
                    if (hoverableObj is HashSet<FrameworkElement> hoverableSet)
                    {
                        hoverableSet.Add(element);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdtOverlayInputManager] Ошибка при регистрации элемента: {ex.Message}");
            }
        }

        public static void UnregisterElement(FrameworkElement element)
        {
            if (element == null) return;

            try
            {
                EnsureInitialized();
                var overlayWindow = Core.OverlayWindow;
                if (overlayWindow == null) return;

                if (overlayWindow.Dispatcher != null && !overlayWindow.Dispatcher.CheckAccess())
                {
                    overlayWindow.Dispatcher.Invoke(new Action(() => UnregisterElement(element)));
                    return;
                }

                if (_clickableElementsField != null)
                {
                    if (_clickableElementsField.GetValue(overlayWindow) is IList clickableList)
                    {
                        clickableList.Remove(element);
                    }
                }

                if (_hoverableElementsField != null)
                {
                    var hoverableObj = _hoverableElementsField.GetValue(overlayWindow);
                    if (hoverableObj is HashSet<FrameworkElement> hoverableSet)
                    {
                        hoverableSet.Remove(element);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HdtOverlayInputManager] Ошибка при отмене регистрации элемента: {ex.Message}");
            }
        }
    }
}
