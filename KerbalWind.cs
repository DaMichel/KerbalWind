/* Needed something simpler than KerbalWeatherSystem. Originally based on some code from KerbalWeatherSystem. Now there is not much left of it ...
   
   Author: DaMichel. KerbalWeatherSystem by silverfox8124.
   
   License: GNU GPL. http://www.gnu.org/licenses/
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
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
    }


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalWind : MonoBehaviour
    {
        // main window
        static Rect MainGUI = new Rect(100,100,-1,-1);
        bool        isWindowOpen = true; //Value for GUI window open
        bool        enableThroughGuiEvent = true;
        bool        enableThroughToolbar = true;
        // gui stuff
        const int   DIRECTION_DIAL_NO_WIND = 4;
        int         windDirectionNumb = DIRECTION_DIAL_NO_WIND;
        float       windSpeed = 0.0f;
        String      windSpeedLabel = "";
        bool        needsUpdate = true;
        GUISkin     skin;
        // toolbar support
        IButton toolbarButton;
        // wind
        Vector3 windDirectionWS; // final wind direction and magnitude in world space
        Vector3 windDirection; // wind in "map" space, y = north, x = east (?)
        // book keeping data
        NavBall navball;
        Quaternion qVessel; // orientation of the vessel relative to world space
        Quaternion qSurfaceToWorld; // orientation of the planet surface under the vessel in world space
        Quaternion qfix = Quaternion.Euler(new Vector3(-90f, 0f, 0f)); // see below

#region boring stuff
        /* Called after the scene is loaded. */
        void Awake()
        {
            Debug.Log("WIND: setting wind function"); //Write to debug
            FARWind.SetWindFunction(GetWind); //Set the WindFunction to the windStuff Function

            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);
            skin.button.padding = new RectOffset(2, 2, 2, 2);
            skin.button.margin = new RectOffset(1, 1, 1, 1);
            skin.box.padding = new RectOffset(2, 2, 2, 2);
            skin.box.margin = new RectOffset(1, 1, 1, 1);
            //skin.window.padding = new RectOffset(2, 2, 2, 2);

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

            Load();
        }


        public void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);

            if (toolbarButton != null)
                toolbarButton.Destroy();
            
            Save();
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


        public void Save()
        {
            PluginConfiguration config = PluginConfiguration.CreateForType<KerbalWind>();
            config.SetValue("Window Position", MainGUI);
            config.save();
        }


        public void Load()
        {
            PluginConfiguration config = PluginConfiguration.CreateForType<KerbalWind>();
            config.load();
            MainGUI = config.GetValue<Rect>("Window Position");
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

        
        /* Called at a fixed time interval determined by the physics time step. */
        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (windDirection != Vector3.zero)
            {
                UpdateCoordinateFrame();
                windDirectionWS = qSurfaceToWorld * windDirection;
            }
            else
                windDirectionWS = Vector3.zero;
        }


        //Called by FAR. Returns wind vector.
        public Vector3 GetWind(CelestialBody body, Part part, Vector3 position)
        {
            return windDirectionWS;
        }


        //Called when the GUI things happen
        void OnGUI()
        {
            if (isWindowOpen)
            {
                GUI.skin = this.skin;
                MainGUI = GUILayout.Window(10, MainGUI, OnWindow, 
                                               windDirectionNumb==DIRECTION_DIAL_NO_WIND ? "No Wind" : "Wind");
            }
        }


        void OnWindow(int windowId)
        {
            GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                    if (GUILayout.Button("-", GUILayout.MinWidth(20))) //Turns down wind speed
                    {
                        needsUpdate = true;
                        windSpeed = Mathf.Max(0.0f, windSpeed - 1.0f);
                    }
                    if (GUILayout.Button("+", GUILayout.MinWidth(20))) //Turns up wind speed
                    {
                        windSpeed += 1.0f;
                        needsUpdate = true;
                    }
                    GUILayout.Box(windSpeedLabel, GUILayout.MinWidth(80));
                GUILayout.EndHorizontal();

                // make a 3x3 button grid
                int oldWindDirectionNumb = windDirectionNumb;
                string[] selStrings = new String[] {"", "N", "", "W", "X", "E", "", "S", ""};
                windDirectionNumb = GUILayout.SelectionGrid(windDirectionNumb, selStrings, 3);
                if (windDirectionNumb != oldWindDirectionNumb) 
                {
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    // X = east
                    // Z = north
                    windDirection = Vector3.zero;
                    switch (windDirectionNumb)
                    {
                        case 7:
                            //windDirectionLabel = "South";
                            windDirection.z = windSpeed;
                            break;
                        case 3:
                            //windDirectionLabel = "West";
                            windDirection.x = windSpeed;
                            break;
                        case 1:
                            //windDirectionLabel = "North";
                            windDirection.z = -windSpeed;
                            break;
                        case 5:
                            //windDirectionLabel = "East";
                            windDirection.x = -windSpeed;
                            break;
                        case 6:
                            //windDirectionLabel = "South West";
                            windDirection.x = windSpeed;
                            windDirection.z = windSpeed;
                            break;
                        case 0:
                            //windDirectionLabel = "North West";
                            windDirection.x = windSpeed;
                            windDirection.z = -windSpeed;
                            break;
                        case 2:
                            //windDirectionLabel = "North East";
                            windDirection.x = -windSpeed;
                            windDirection.z = -windSpeed;
                            break;
                        case 8:
                            //windDirectionLabel = "South East";
                            windDirection.x = -windSpeed;
                            windDirection.z = windSpeed;
                            break;
                        default:
                            //windDirectionLabel = "No Wind";
                            break;
                    }
                    windSpeedLabel = windSpeed.ToString("F0") + " m/s";
                    needsUpdate = false;
                }
                //GUILayout.Label(windDirectionLabel);
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
        
#if false
        /* Called every frame */
        void Update()
        {
        }

//Called when the drawing happens
        private void OnDraw() 
        {
            
        }

        /* Called after Awake. */
        void Start()
        {
            
        }
#endif
    }
}
