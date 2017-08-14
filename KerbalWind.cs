/*---------------------------------------------------------------------------
Adds wind to the game. It opens a dialog box where you can set wind direction and speed. 
Inspired by KerbalWeatherSystem by silverfox8124. But this is much simpler, omitting the 
actual weather simulation.
   
Author: DaMichel, silverfox8124
   
License: The code is subject to the MIT license (see below). In addition
that creators of derivative work must give credit to silverfox8124 and DaMichel.
------------------------------------------------
Copyright (c) 2015 DaMichel, silverfox8124

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
---------------------------------------------------------------------------*/

using System;
using System.ComponentModel;
using System.Text;
using UnityEngine;
using KSP.IO;
using System.Reflection;


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

        public static float Floor(float x, int digits)
        {
            int lol = 1;
            while (digits > 0)
            {
                lol *= 10;
                --digits;
            }
            return (float)((int)(x*lol))/lol;
        }
    }

    /* https://en.wikipedia.org/wiki/Continuous_gusts
     * for now neglecting altitude dependence
     */
    class ContinuousGustsModel
    {
        // directions:
        //  u = parallel to average wind velocity vector 
        //  w = vertical
        //  v = perpendicular to u and w
        float Y_u, Y_v, Y_w; // output 
        float Lu, Lv, Lw;    // length scales
        float sigma_u, sigma_v, sigma_w;  // standard deviation
        float turbulence_severity = 0f; // in m/s. Note, 1 feet/s = 0.305 m/s
        Vector3 output = Vector3.zero;
        System.Random rand = new System.Random();
        
        class LateralProcessState
        {
            public float[] Y1 = { 0f, 0f };
            public float[] Y2 = { 0f, 0f };
        };

        LateralProcessState Y_v_state = new LateralProcessState();
        LateralProcessState Y_w_state = new LateralProcessState();

        public Vector3 gustMagnitude
        {
            get 
            {
                return output;
            }
        }

        private float randGauss()
        {
            // http://stackoverflow.com/questions/218060/random-gaussian-variables
            double u1 = rand.NextDouble();
            double u2 = rand.NextDouble();
            double r = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            return (float)r;
        }

        public void Init(float turbulence_severity) 
        {   
            this.turbulence_severity = turbulence_severity;
            Y_u = Y_v = Y_w = 0f;
        }

        private void ComputeProcessParameters(float altitude, float altitude_above_ground)
        {
            /* taken from wikipedia https://en.wikipedia.org/wiki/Continuous_gusts */
            const float LENGTH_SCALE_THOUSANDFT = 300f;
            const float LOW_ALTITUDE_THRESHOLD = 300f;
            const float M_TO_FEET = 3.28084f;
            if (altitude_above_ground < 0f)
                altitude_above_ground = 0f; // because the length scales Lu,v,w are proportional to it.
            float h = altitude_above_ground * M_TO_FEET;
            if (altitude_above_ground < LOW_ALTITUDE_THRESHOLD)
            {
                // evaluates to Lu = 2Lv = 2Lw = 1000 ft at 1000 ft AGL.
                Lw = 0.5f * altitude_above_ground;
                Lu = altitude_above_ground / Mathf.Pow(0.177f + 0.000823f*h, 1.2f);
                Lv = 0.5f * Lu;
                sigma_w = turbulence_severity;
                sigma_u = sigma_w / Mathf.Pow(0.177f + 0.000823f*h, 0.4f);
                sigma_v = sigma_u;
            }
            else
            {
                Lu = LENGTH_SCALE_THOUSANDFT;
                Lv = Lw = 0.5f * Lu;
                sigma_u = turbulence_severity;
                sigma_v = sigma_w = sigma_u;
            }
            // standard deviation of wind speed is about 0.1 * W20. W20 is the wind speed at 20 ft.
        }

        /* Output Y is a random process, the power spectral density of which should
        *  closely approximate the power spectral density of the Dryden model.
        */
        private float ComputeProcess(float T, float L, float Y)
        {
            if (T+L > 0f)
            {
                float B = Mathf.Sqrt(2f*L*T)/(T+L);
                float A = L/(T+L);
                Y = B*randGauss() + A*Y;
            }
            return Y;
        }

        /* Output Y is a random process, the power spectral density of which should
        *  closely approximate the power spectral density of the Dryden model.
        *  
        *  This is for the lateral and vertical wind components. 
        */
        private float ComputeLateralProcess(float T, float L, float Y, ref LateralProcessState s)
        {
            if (T > 0f && L > 0f)
            {
                float X = randGauss();
                float L1 = 1.2f*L;
                float L2 = 3f*L;
                float F1 = 9f/8f;
                float F2 = -1f/8f;
                float N1 = Mathf.Sqrt(2f*L1);
                float N2 = Mathf.Sqrt(Mathf.Sqrt(2f*L2));
                float a1 = L1/(T+L1);
                float a2 = L2/(T+L2);
                float b1 = N1*T/(T+L1);
                float b2 = N2*T/(T+L2);
                s.Y1[0] = b1 * X       + a1 * s.Y1[0];
                s.Y2[0] = b2 * X       + a2 * s.Y2[0];
                s.Y2[1] = b2 * s.Y2[0] + a2 * s.Y2[1];
                Y = F1 * s.Y1[0] + F2 * s.Y2[1];
                return Y / Mathf.Sqrt(T);
            }
            return Y;
        }

        private float ComputeAveragedSigma(float sigma, float T, float L)
        {
            /* When the sampling distances is much longer than the correlation length L, 
             * the output sequence looks like uncorrelated white noise. 
             * Furthermore, I assume that fluctuations of the output process are averaged out over 
             * the length T. In this case, the discretized output looks like the input but with the
             * variance reduced by 1/T in good approximation. That is, the output is band limited 
             * white noise with cutoff frequency 1/T.
             * Gameplay wise this means that vibrations, which may be significant, that would occur 
             * in the duration of one frame, are neglected. Not a big deal unless the behavior of
             * the vehicle is non-linear.
             */
            return (T > L) ? sigma*Mathf.Sqrt(L/T) : sigma;
        }

        public void Update(float dt, Vector3 wind_velocity, Vector3 vehicle_velocity, float altitude, float altitude_above_ground)
        {
            ComputeProcessParameters(altitude, altitude_above_ground);
            float tas = (wind_velocity + vehicle_velocity).magnitude; // the true air speed
            float T = dt * tas; // distance traveled within the frozen turbulence field

            Y_u = ComputeProcess(T, Lu, Y_u);
            Y_v = ComputeLateralProcess(T, Lv, Y_v, ref Y_v_state);
            Y_w = ComputeLateralProcess(T, Lw, Y_w, ref Y_w_state);

            float avg_sigma_u = ComputeAveragedSigma(sigma_u, T, Lu);
            float avg_sigma_v = ComputeAveragedSigma(sigma_v, T, Lv);
            float avg_sigma_w = ComputeAveragedSigma(sigma_w, T, Lw);

            output.x = Y_u*avg_sigma_u;
            output.y = Y_v*avg_sigma_v;
            output.z = Y_w*avg_sigma_w;

            object[] args = { T, Y_u, Y_v, Y_w, sigma_u, sigma_v, sigma_w, altitude_above_ground, Lu, Lv, Lw };
            //Debug.Log(String.Format("Gusts Model: alt = {7}  T = {0}\n   Y_u={1}\n   Y_v={2}\n   Y_w={3}\n   sigma_u={4}  sigma_v={5}  sigma_w={6}\n   Lu={8}  Lv={9}  Lw={10}\n", args));
        }
    };


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalWind : DaMichelToolbarSuperWrapper.PluginWithToolbarSupport
    {
        // main window
        Rect        windowRect = new Rect(100,100,-1,-1);
        bool        isWindowOpen = true;
        // gui stuff
        const int   DIRECTION_DIAL_NO_WIND = 4;
        int         windDirectionId = DIRECTION_DIAL_NO_WIND;
        float       windSpdGuiState = 0.0f; // the value that is being increased when you hit the +/- buttons
        float       windSpd = 0.0f;
        string      windSpdLabel = "";
        string      windDirLabel = "x";
        string      windowTitle  = "";
        float       gustsSeverityGuiState = 0.0f; // the value that is being increased when you hit the +/- buttons
        string      gustsSeverityGuiLabel = "";
        bool        needsUpdate = true;
        GUISkin     skin;
        // wind
        Vector3 windVectorWS; // final wind direction and magnitude in world space
        Vector3 gustsVectorWS;
        Vector3 windVector; // wind in "map" space, y = north, x = east (?)
        Vector3 windDirection;
        bool    windEnabled = false;
        // book keeping data
        Matrix4x4  mSurfaceToWorld = Matrix4x4.identity; // orientation of the planet surface under the vessel in world space
        ContinuousGustsModel gustsmodel = new ContinuousGustsModel();

        bool RegisterWithFAR()
        {
            try
            {
                Type FARWind = null;
                Type WindFunction = null;
                foreach (var assembly in AssemblyLoader.loadedAssemblies)
                {
                    if (assembly.name == "FerramAerospaceResearch")
                    {
                        var types = assembly.assembly.GetExportedTypes();
                        foreach (Type t in types)
                        {
                            if (t.FullName.Equals("FerramAerospaceResearch.FARWind"))
                            {
                                FARWind = t;
                            }
                            if (t.FullName.Equals("FerramAerospaceResearch.FARWind+WindFunction"))
                            {
                                WindFunction = t;
                            }
                        }
                    }
                }
                if (FARWind == null)
                {
                    Debug.LogError("KerbalWind: unable to find FerramAerospaceResearch.FARWind");
                    return false;
                }
                if (WindFunction == null)
                {
                    Debug.LogError("KerbalWind: unable to find FerramAerospaceResearch.FARWind+WindFunction");
                    return false;
                }
                MethodInfo SetWindFunction = FARWind.GetMethod("SetWindFunction");
                if (SetWindFunction == null)
                {
                    Debug.LogError("KerbalWind: unable to find FARWind.SetWindFunction");
                    return false;
                }
                var del = Delegate.CreateDelegate(WindFunction, this, typeof(KerbalWind).GetMethod("GetTheWind"), true);
                SetWindFunction.Invoke(null, new object[] { del });
                return true; // jump out!
            }
            catch (Exception e)
            {
                Debug.LogError("KerbalWind: unable to register with FerramAerospaceResearch. Exception thrown: "+e.ToString());
            }
            return false;
        }

#region boring stuff
        protected override DaMichelToolbarSuperWrapper.ToolbarInfo GetToolbarInfo()
        {
            return new DaMichelToolbarSuperWrapper.ToolbarInfo {
                name = "KerbalWind",
                tooltip = "KerbalWind Show/Hide Gui",
                toolbarTexture = "KerbalWind/toolbarbutton",
                launcherTexture = "KerbalWind/launcherbutton",
                visibleInScenes = new GameScenes[] { GameScenes.FLIGHT }
            };
        }

        void Awake()
        {
            skin = (GUISkin)GUISkin.Instantiate(HighLogic.Skin);
            skin.button.padding = new RectOffset(2, 2, 2, 2);
            skin.button.margin = new RectOffset(1, 1, 1, 1);
            skin.box.padding = new RectOffset(2, 2, 2, 2);
            skin.box.margin = new RectOffset(1, 1, 1, 1);
            skin.textField.margin = new RectOffset(3,1,1,1);
            skin.textField.padding = new RectOffset(4,2,1,0);

            if (!RegisterWithFAR())
            {
                this.enabled = false;
            }

            LoadSettings();
            InitializeToolbars();
            OnGuiVisibilityChange();

            gustsmodel.Init(0f);
            ComputeWindVector();
        }


        public void OnDestroy()
        {
            SaveSettings();
            TearDownToolbars();
        }


        protected override  void OnGuiVisibilityChange()
        {
            isWindowOpen = isGuiVisible;
        }


        void SaveSettings()
        {
            ConfigNode settings = new ConfigNode();
            settings.name = "KERBAL_WIND_SETTINGS";
            SaveMutableToolbarSettings(settings);
            SaveImmutableToolbarSettings(settings);
            settings.AddValue("windowRect.xMin", windowRect.xMin);
            settings.AddValue("windowRect.yMin", windowRect.yMin);
            settings.AddValue("windDirectionId", windDirectionId);
            settings.AddValue("windSpdGuiState", windSpdGuiState);
            settings.AddValue("gustsSeverity", gustsSeverityGuiState);
            settings.Save(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(KerbalWind)) + "/settings.cfg");
        }


        void LoadSettings()
        {
            ConfigNode settings = ConfigNode.Load(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(KerbalWind)) + "/settings.cfg");
            if (settings != null)
            {
                float x = windowRect.xMin, y = windowRect.yMin; // making structs immutable sure was a good idea ...
                Util.TryReadValue(ref x, settings, "windowRect.xMin");
                Util.TryReadValue(ref y, settings, "windowRect.xMin");
                windowRect = new Rect(x, y, windowRect.width, windowRect.height); // it's so much safer and reduces the amount of awkward code one has to write ... 
                LoadMutableToolbarSettings(settings);
                LoadImmutableToolbarSettings(settings);
                Util.TryReadValue(ref windDirectionId, settings, "windDirectionId");
                Util.TryReadValue(ref windSpdGuiState, settings, "windSpdGuiState");
                Util.TryReadValue(ref gustsSeverityGuiState, settings, "gustsSeverity");
            }
            windSpd = (float)(int)windSpdGuiState; // round to next greater integer so we go relatively slowly in 1m/s steps while the MB is down.
            windSpdLabel = windSpd.ToString("F0");
            gustsSeverityGuiLabel = gustsSeverityGuiState.ToString("F1");
        }
#endregion

        bool UpdateCoordinateFrame()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return false;
            Vector3 east = vessel.east;
            Vector3 north = vessel.north;
            Vector3 up   = vessel.upAxis;
            mSurfaceToWorld[0,2] = east.x;
            mSurfaceToWorld[1,2] = east.y;
            mSurfaceToWorld[2,2] = east.z;
            mSurfaceToWorld[0,1] = up.x;
            mSurfaceToWorld[1,1] = up.y;
            mSurfaceToWorld[2,1] = up.z;
            mSurfaceToWorld[0,0] = north.x;
            mSurfaceToWorld[1,0] = north.y;
            mSurfaceToWorld[2,0] = north.z;

#if DEBUG
            if (Input.GetKeyDown(KeyCode.O))
            {
                Quaternion qVessel;
                if (vessel.ReferenceTransform != null)
                {
                    qVessel = vessel.ReferenceTransform.rotation;
                }
                else
                {
                    qVessel = vessel.transform.rotation;
                }
                Vector3 vx = qVessel * Util.x; // sideways
                Vector3 vy = qVessel * Util.y; // the longitudinal axis
                Vector3 vz = qVessel * Util.z; // down for a plane
                Vector3 sx = mSurfaceToWorld.GetColumn(0);
                Vector3 sy = mSurfaceToWorld.GetColumn(1);
                Vector3 sz = mSurfaceToWorld.GetColumn(2);
                float xdoty = Vector3.Dot(sx, sy);
                float xdotz = Vector3.Dot(sx, sz);
                float ydotz = Vector3.Dot(sy, sz);
                StringBuilder sb = new StringBuilder(8);
                sb.AppendLine("KerbalWind:");
                sb.AppendLine("       vx = " + vx.ToString("F2"));
                sb.AppendLine("       vy = " + vy.ToString("F2"));
                sb.AppendLine("       vz = " + vz.ToString("F2"));
                sb.AppendLine("       sx = " + sx.ToString("F2"));
                sb.AppendLine("       sy = " + sy.ToString("F2")); // up
                sb.AppendLine("       sz = " + sz.ToString("F2"));
                sb.AppendLine("       xdoty = " + xdoty.ToString("F2"));
                sb.AppendLine("       xdotz = " + xdotz.ToString("F2"));
                sb.AppendLine("       ydotz = " + ydotz.ToString("F2"));
                sb.AppendLine("ship_upAxis             = "+((Vector3)FlightGlobals.ship_upAxis).ToString("F3"));
                sb.AppendLine("upAxis                  = "+((Vector3)FlightGlobals.upAxis).ToString("F3"));
                sb.AppendLine("getUpAxis               = "+((Vector3)FlightGlobals.getUpAxis()).ToString("F3"));
                sb.AppendLine("vessel.upAxis           = "+((Vector3)vessel.upAxis).ToString("F3"));
                //sb.AppendLine("       vy*wind = " + (Vector3.Dot(vy,windDirectionWS)/windSpeed).ToString("F2"));
                //sb.AppendLine("       vz*wind = " + (Vector3.Dot(vz,windDirectionWS)/windSpeed).ToString("F2"));
                Debug.Log(sb.ToString());
            }
#endif
            return true;
        }


        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (windEnabled)
            {
                UpdateCoordinateFrame();
                windVectorWS = mSurfaceToWorld * windVector;
                if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null)
                {
                    Vessel vessel = FlightGlobals.ActiveVessel;
                    Vector3 vehicle_velocity = vessel.srf_velocity;
                    double radarAltitude = vessel.altitude - Math.Max(0, vessel.terrainAltitude); // terrainAltitude is the deviation of the terrain from the sea level.
                    float dt = Time.deltaTime;
                    gustsmodel.Update(dt, windVectorWS, vehicle_velocity, (float)vessel.altitude, (float)radarAltitude);
                    Vector3 Yuvw = gustsmodel.gustMagnitude;
                    gustsVectorWS = Yuvw[0] * windDirection;
                    gustsVectorWS += Yuvw[2] * vessel.upAxis;
                    gustsVectorWS += Yuvw[1] * Vector3.Cross(windDirection, vessel.upAxis);
                }
                else
                {
                    gustsVectorWS = Vector3.zero;
                }
            }
            else
            {
                windVectorWS = Vector3.zero;
                gustsVectorWS = Vector3.zero;
            }
        }


        //Called by FAR. Returns wind vector.
        public Vector3 GetTheWind(CelestialBody body, Part part, Vector3 position)
        {
            if (!part || (part.partBuoyancy && part.partBuoyancy.splashed))
            {
                return Vector3.zero;
            }
            else
            {
                if (part.vessel == FlightGlobals.ActiveVessel)
                    return windVectorWS + gustsVectorWS;
                else
                    return windVectorWS;
            }
        }


        void ComputeWindVector()
        {
            // X = north, consistent with vessel axes where x points north for vessels on the KSC runway pointing east
            // Z = east
            windDirection = Vector3.zero;
            windDirLabel = "x";
            windEnabled  = true;
            //string dirLabel2 = "";
            switch (windDirectionId)
            {
                case 4:
                    windEnabled = false;
                    break;
                case 7: // S
                    windDirection.x = -1;
                    windDirLabel = "\u2193";
                    //dirLabel2  = "N";
                    break;
                case 3: // W
                    windDirection.z = -1;
                    windDirLabel = "\u2190";
                    //dirLabel2  = "E";
                    break;
                case 1: // N
                    windDirection.x = 1;
                    windDirLabel = "\u2191";
                    //dirLabel2 = "S";
                    break;
                case 5: // E
                    windDirection.z = 1;
                    windDirLabel = "\u2192";
                    //dirLabel2 = "W";
                    break;
                case 6: // SW
                    windDirection.z = -1;
                    windDirection.x = -1;
                    windDirLabel = "\u2199";
                    //dirLabel2 = "NE";
                    break;
                case 0: // NW
                    windDirection.z = -1;
                    windDirection.x = 1;
                    windDirLabel = "\u2196";
                    //dirLabel2 = "SE";
                    break;
                case 2: // NE
                    windDirection.z = 1;
                    windDirection.x = 1;
                    windDirLabel = "\u2197";
                    //dirLabel2 = "SW";
                    break;
                case 8: // SE
                    windDirection.z = 1;
                    windDirection.x = -1;
                    windDirLabel = "\u2198";
                    //dirLabel2 = "NW";
                    break;
            }
            windDirection.Normalize();
            windSpd = Util.Floor(windSpdGuiState, 0); // round to next greater integer so we go relatively slowly in 1m/s steps while the MB is down.
            windVector = windSpd*windDirection;
            gustsmodel.Init(Util.Floor(gustsSeverityGuiState,1));
        }


        void OnGUI()
        {
            if (isWindowOpen)
            {
                //Debug.Log(String.Format("KerbalWind window @{0},{1}, size: {2},{3}", windowRect.xMin, windowRect.yMin, windowRect.width, windowRect.height));
                GUI.skin = this.skin;
                windowRect = GUILayout.Window(this.GetHashCode(), windowRect, MakeMainWindow, windowTitle);
                float left = Mathf.Clamp(windowRect.x, 0, Screen.width-windowRect.width);
                float top = Mathf.Clamp(windowRect.y, 0, Screen.height-windowRect.height);
                windowRect = new Rect(left, top, windowRect.width, windowRect.height);
            }
        }


        private void MakeNumberEditField(ref string value_as_text, ref float value, ref bool flag_changed, int digits)
        {
                string newlabel = GUILayout.TextField(value_as_text, GUILayout.MinWidth(40));
                if (value_as_text != newlabel)
                {
                    float newvalue;
                    if (float.TryParse(newlabel, out newvalue))
                    {
                        value = newvalue;
                        flag_changed = true;
                    }
                    value_as_text = newlabel;
                }
                bool hitMinusButton = GUILayout.RepeatButton("-", GUILayout.MinWidth(20)); //Turns down wind speed
                bool hitPlusButton  = GUILayout.RepeatButton("+", GUILayout.MinWidth(20)); //Turns up wind speed
                if (hitPlusButton || hitMinusButton)
                {
                    int lol = 1;
                    int tmp = digits;
                    while (tmp > 0)
                    {
                        lol *= 10;
                        tmp--;
                    }
                    if (hitMinusButton)
                        value = Mathf.Max(0.0f, value - 0.1f/lol);
                    else
                        value += 0.1f/lol;
                    value_as_text = value.ToString("F"+digits.ToString());
                    flag_changed = true;
                }
        }


        void MakeMainWindow(int id)
        {
            GUILayout.BeginVertical();
                // make a 3x3 button grid
                int oldWindDirectionNumb = windDirectionId;
                string[] selStrings = new String[] {"", "N", "", "W", windDirLabel, "E", "", "S", ""};
                //string[] selStrings = new String[] {"", "", "", "", windDirLabel, "", "", "", ""};
                selStrings[windDirectionId] = windDirLabel;
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

                //if (windDirectionId==DIRECTION_DIAL_NO_WIND)
                //    windowTitle = "No Wind";
                //else
                windowTitle = (windVectorWS+gustsVectorWS).magnitude.ToString("F1") + " m/s";

                GUILayout.Space(4);
                GUILayout.Label("Speed", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    MakeNumberEditField(ref windSpdLabel, ref windSpdGuiState, ref needsUpdate, 0);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                GUILayout.Label("Turbulence", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    MakeNumberEditField(ref gustsSeverityGuiLabel, ref gustsSeverityGuiState, ref needsUpdate, 1);
                GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
