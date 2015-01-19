/*---------------------------------------------------------------------------
I (DaMichel) needed something simpler than KerbalWeatherSystem. Originally 
based on code from KerbalWeatherSystem by silverfox8124.
   
Author: DaMichel, silverfox8124
   
License: The following code is based off the work of SilverFox8124, and used 
with permission under license of: All Right's Reserved, 2014(c)
---------------------------------------------------------------------------*/

using System;
using System.ComponentModel;
using UnityEngine;
using KSP.IO;
using ferram4;


namespace KerbalWind
{
    public static class Util
    {
        public static Vector3 x = Vector3.right;
        public static Vector3 y = Vector3.up;
        public static Vector3 z = Vector3.forward;
        public static void TryReadValue<T>(ref T target, ConfigNode node, string name)
        {
            if (node.HasValue(name))
            {
                try
                {
                    target = (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromString(node.GetValue(name));
                }
                catch
                {
                    // just skip over it
                }
            }
            // skip again
        }
    }


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalWind : MonoBehaviour
    {
        // main window
        Rect        windowRect = new Rect(100,100,-1,-1);
        bool        isWindowOpen = true;
        bool        enableThroughGuiEvent = true;
        bool        enableThroughToolbar = true;
        // gui stuff
        const int   DIRECTION_DIAL_NO_WIND = 4;
        int         windDirectionId = DIRECTION_DIAL_NO_WIND;
        float       windSpd = 0.0f;
        String      windSpdLabel = "";
        bool        needsUpdate = true;
        GUISkin     skin;
        // toolbar support
        IButton toolbarButton;
        // wind
        Vector3 windVectorWS; // final wind direction and magnitude in world space
        Vector3 windVector; // wind in "map" space, y = north, x = east (?)
        // book keeping data
        NavBall navball;
        Quaternion qVessel; // orientation of the vessel relative to world space
        Quaternion qSurfaceToWorld; // orientation of the planet surface under the vessel in world space
        Quaternion qfix = Quaternion.Euler(new Vector3(-90f, 0f, 0f)); // see below

#region boring stuff
        void Awake()
        {
            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);
            skin.button.padding = new RectOffset(2, 2, 2, 2);
            skin.button.margin = new RectOffset(1, 1, 1, 1);
            skin.box.padding = new RectOffset(2, 2, 2, 2);
            skin.box.margin = new RectOffset(1, 1, 1, 1);

            if (ToolbarManager.ToolbarAvailable)
            {
                toolbarButton = ToolbarManager.Instance.add("KerbalWind", "KerbalWind");
                toolbarButton.TexturePath = "KerbalWind/toolbarbutton";
                toolbarButton.ToolTip = "KerbalWind Show/Hide";
                toolbarButton.Visibility = new GameScenesVisibility(GameScenes.FLIGHT);
                toolbarButton.Enabled = true;
                toolbarButton.OnClick += (e) =>
                {
                    enableThroughToolbar = !enableThroughToolbar;
                    isWindowOpen = enableThroughGuiEvent && enableThroughToolbar;
                };
            }
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            FARWind.SetWindFunction(WindReturnCallback);

            LoadSettings();

            ComputeWindVector();
        }


        public void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);

            if (toolbarButton != null)
                toolbarButton.Destroy();

            SaveSettings();
        }


        void OnHideUI()
        {
            enableThroughGuiEvent = false;
            isWindowOpen = enableThroughGuiEvent && enableThroughToolbar;
        }


        void OnShowUI()
        {
            enableThroughGuiEvent = true;
            isWindowOpen = enableThroughGuiEvent && enableThroughToolbar;
        }


        void SaveSettings()
        {
            ConfigNode settings = new ConfigNode();
            settings.name = "KERBAL_WIND_SETTINGS";
            settings.AddValue("windowRect.xMin", windowRect.xMin);
            settings.AddValue("windowRect.yMin", windowRect.yMin);
            settings.AddValue("enableThroughToolbar", enableThroughToolbar);
            settings.AddValue("windDirectionId", windDirectionId);
            settings.AddValue("windSpd", windSpd);
            settings.Save(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(KerbalWind)) + "/settings.cfg");
        }


        void LoadSettings()
        {
            ConfigNode settings = new ConfigNode();
            settings = ConfigNode.Load(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(KerbalWind)) + "/settings.cfg");
            if (settings != null)
            {
                float x = windowRect.xMin, y = windowRect.yMin; // making structs immutable sure was a good idea ...
                Util.TryReadValue(ref x, settings, "windowRect.xMin");
                Util.TryReadValue(ref y, settings, "windowRect.xMin");
                windowRect = new Rect(x, y, windowRect.width, windowRect.height); // it's so much safer and reduces the amount of awkward code one has to write ... 
                Util.TryReadValue(ref enableThroughToolbar, settings, "enableThroughToolbar");
                isWindowOpen = enableThroughGuiEvent && enableThroughToolbar;
                Util.TryReadValue(ref windDirectionId, settings, "windDirectionId");
                Util.TryReadValue(ref windSpd, settings, "windSpd");
            }
        }
#endregion

        bool UpdateCoordinateFrame()
        {
            if (navball == null)
                navball = FlightUIController.fetch.GetComponentInChildren<NavBall>();
            if (navball == null)
                return false;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return false;
            if (vessel.ReferenceTransform != null)
            {
                qVessel = vessel.ReferenceTransform.rotation;
            }
            else
            {
                qVessel = vessel.transform.rotation;
            }
            /* Lets use the navball to get the surface orientation.
               This is a bit obscure. But in principle the navball contains the orientation of 
               the surface relative to the vessel. So we can chain the transforms 
               (vessel->world)*(surface->vessel). "world" is a pretty much arbitrary reference 
               frame. Vessel position is given relative to this frame and wind is also expected
               to be given in this frame.
            */
            qSurfaceToWorld = qVessel * qfix * navball.relativeGymbal;

#if DEBUG
            if (Input.GetKeyDown(KeyCode.O))
            {
                Vector3 vx = qVessel * Util.x; // sideways
                Vector3 vy = qVessel * Util.y; // the longitudinal axis for a plane
                Vector3 vz = qVessel * Util.z; // down for a plane (?)
                Vector3 sx = qSurfaceToWorld * Util.x;
                Vector3 sy = qSurfaceToWorld * Util.y;
                Vector3 sz = qSurfaceToWorld * Util.z;
                StringBuilder sb = new StringBuilder(8);
                sb.AppendLine("KerbalWind:");
                sb.AppendLine("       vx = " + vx.ToString("F2"));
                sb.AppendLine("       vy = " + vy.ToString("F2"));
                sb.AppendLine("       vz = " + vz.ToString("F2"));
                sb.AppendLine("       sx = " + sx.ToString("F2")); // probably vertical, pointing down?
                sb.AppendLine("       sy = " + sy.ToString("F2")); // probably east
                sb.AppendLine("       sz = " + sz.ToString("F2")); // probably north
                sb.AppendLine("       vy*wind = " + (Vector3.Dot(vy,windDirectionWS)/windSpeed).ToString("F2"));
                sb.AppendLine("       vz*wind = " + (Vector3.Dot(vz,windDirectionWS)/windSpeed).ToString("F2"));
                Debug.Log(sb.ToString());
            }
#endif
            return true;
        }


        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (windVector != Vector3.zero)
            {
                UpdateCoordinateFrame();
                windVectorWS = qSurfaceToWorld * windVector;
            }
            else
                windVectorWS = Vector3.zero;
        }


        //Called by FAR. Returns wind vector.
        public Vector3 WindReturnCallback(CelestialBody body, Part part, Vector3 position)
        {
            return windVectorWS;
        }


        void ComputeWindVector()
        {
            // X = east
            // Z = north
            windVector = Vector3.zero;
            switch (windDirectionId)
            {
                case 7: // S
                    windVector.z = windSpd;
                    break;
                case 3: // W
                    windVector.x = windSpd;
                    break;
                case 1: // N
                    windVector.z = -windSpd;
                    break;
                case 5: // E
                    windVector.x = -windSpd;
                    break;
                case 6: // SW
                    windVector.x = windSpd;
                    windVector.z = windSpd;
                    break;
                case 0: // NW
                    windVector.x = windSpd;
                    windVector.z = -windSpd;
                    break;
                case 2: // NE
                    windVector.x = -windSpd;
                    windVector.z = -windSpd;
                    break;
                case 8: // SE
                    windVector.x = -windSpd;
                    windVector.z = windSpd;
                    break;
            }
            windSpdLabel = windSpd.ToString("F0") + " m/s";
        }


        void OnGUI()
        {
            if (isWindowOpen)
            {
                GUI.skin = this.skin;
                windowRect = GUILayout.Window(10, windowRect, MakeMainWindow, 
                                                  windDirectionId==DIRECTION_DIAL_NO_WIND ? "No Wind" : "Wind");
            }
        }


        void MakeMainWindow(int id)
        {
            GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                    if (GUILayout.Button("-", GUILayout.MinWidth(20))) //Turns down wind speed
                    {
                        needsUpdate = true;
                        windSpd = Mathf.Max(0.0f, windSpd - 1.0f);
                    }
                    if (GUILayout.Button("+", GUILayout.MinWidth(20))) //Turns up wind speed
                    {
                        windSpd += 1.0f;
                        needsUpdate = true;
                    }
                    GUILayout.Box(windSpdLabel, GUILayout.MinWidth(80));
                GUILayout.EndHorizontal();

                // make a 3x3 button grid
                int oldWindDirectionNumb = windDirectionId;
                string[] selStrings = new String[] {"", "N", "", "W", "X", "E", "", "S", ""};
                windDirectionId = GUILayout.SelectionGrid(windDirectionId, selStrings, 3);
                if (windDirectionId != oldWindDirectionNumb) 
                {
                    needsUpdate = true;
                }
                if (needsUpdate)
                {
                    ComputeWindVector();
                    needsUpdate = false;
                }
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
