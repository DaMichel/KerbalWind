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
using Noise;
using EdyCommonTools;

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



    /*
Light turb
key = 0 0 0.003 0.003
key = 1000 3 0.00225 0.00225
key = 2000 4.5 0.000675 0.000675
key = 12000 3 -0.0001166667 -0.0001166667
key = 18000 2.5 0 0

Moderate
key = 0 0 0.006 0.006
key = 1000 6 0.0045 0.0045
key = 2000 9 0.001428571 0.001428571
key = 30000 5 -0.0001547619 -0.0001547619
key = 45000 2.5 0 0

Severe
key = 0 0 0.006 0.006
key = 1000 6 0.006 0.006
key = 3000 18 0.002962963 0.002962963
key = 30000 16 -7.037037E-05 -7.037037E-05
key = 45000 15 -0.0002119048 -0.0002119048
key = 80000 2.5 0 0

        Alt wind
key = 0 1 0.2 0.2
key = 10 3 -0.025 -0.025
key = 20 0.5 -0.02 -0.02
key = 70 11 -0.1866667 -0.1866667
key = 88 0.5 -0.06249999 -0.06249999
key = 100 6 0.1166667 0.1166667
key = 120 1.5 0 0


    /* https://en.wikipedia.org/wiki/Continuous_gusts
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
        Vector3 output = Vector3.zero;
        float turbSev = 0f;
        System.Random rand = new System.Random();

        FloatCurve LightTurbulence;
        FloatCurve ModerateTurbulence;
        FloatCurve SevereTurbulence;

        FloatCurve AltitudeMultiplier;
        
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

        public float turbulenceSev
        {
            get
            {
                return turbSev;
            }
        }

        public float altMultiplier(float altitude)
        {
            return AltitudeMultiplier.Evaluate(altitude);
        }

        public float randGauss()
        {
            // http://stackoverflow.com/questions/218060/random-gaussian-variables
            double u1 = 1f - rand.NextDouble();
            double u2 = 1f - rand.NextDouble();
            double r = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            return (float)r;
        }

        public void Init() 
        {
            Y_u = Y_v = Y_w = 0f;

            if (AltitudeMultiplier == null)
            {
                Keyframe[] lightKeys = {
                new Keyframe(0f, 0f, 0.003f, 0.003f),
                new Keyframe(1000f, 3f, 0.00225f, 0.00225f),
                new Keyframe(2000f, 4.5f, 0.000675f, 0.000675f),
                new Keyframe(12000f, 3f, -0.0001166667f, -0.0001166667f),
                new Keyframe(18000f, 2.5f, 0f, 0f),
                };
                LightTurbulence = new FloatCurve(lightKeys);

                Keyframe[] moderateKeys = {
                new Keyframe(0f, 0f, 0.006f, 0.006f),
                new Keyframe(1000f, 6f, 0.0045f, 0.0045f),
                new Keyframe(2000f, 9f, 0.001428571f, 0.001428571f),
                new Keyframe(30000f, 5f, -0.0001547619f, -0.0001547619f),
                new Keyframe(45000f, 2.5f, 0f, 0f),
                };
                ModerateTurbulence = new FloatCurve(moderateKeys);

                Keyframe[] severeKeys = {
                new Keyframe(0f, 0f, 0.006f, 0.006f),
                new Keyframe(1000f, 6f, 0.006f, 0.006f),
                new Keyframe(3000f, 18f, 0.002962963f, 0.002962963f),
                new Keyframe(30000f, 16f, -7.037037E-05f, -7.037037E-05f),
                new Keyframe(45000f, 15f, -0.0002119048f, -0.0002119048f),
                new Keyframe(80000f, 2.5f, 0f, 0f),
                };
                SevereTurbulence = new FloatCurve(severeKeys);

                Keyframe[] altitudeKeys = {
                new Keyframe(100f, 1f, 0f, 0f),
                new Keyframe(10000f, 2.5f, -2.424242e-05f, -2.424242e-05f),
                new Keyframe(20000f, 0.5f, -4.999994e-06f, -4.999994e-06f),
                new Keyframe(70000f, 10f, -0.0001688889f, -0.0001688889f),
                new Keyframe(88000f, 0.5f, -6.25e-5f, -6.25e-5f),
                new Keyframe(100000f, 6f, 0.0001166667f, 0.0001166667f),
                new Keyframe(120000f, 1.5f, 0f, 0f),
                };
                AltitudeMultiplier = new FloatCurve(altitudeKeys);
            }
        }

        private void ComputeProcessParameters(float altitude, float altitude_above_ground, float wind_speed)
        {
            /* taken from wikipedia https://en.wikipedia.org/wiki/Continuous_gusts */
            const float LENGTH_SCALE_THOUSANDFT = 300f;
            const float LOW_ALTITUDE_THRESHOLD = 300f;
            const float HIGH_ALTITUDE_THRESHOLD = 600f;
            const float M_TO_FEET = 3.28084f;
            if (altitude_above_ground < 0f)
                altitude_above_ground = 0f; // because the length scales Lu,v,w are proportional to it.
            float h = altitude_above_ground * M_TO_FEET;

            float turbulence_severity = wind_speed * 0.1f;

            if (altitude_above_ground < LOW_ALTITUDE_THRESHOLD)
            {
                // evaluates to Lu = 2Lv = 2Lw = 1000 ft at 1000 ft AGL.
                Lw = 0.5f * altitude_above_ground;
                Lu = altitude_above_ground / Mathf.Pow(0.177f + 0.000823f*h, 1.2f);
                Lv = 0.5f * Lu;
                sigma_w = Mathf.Max(turbulence_severity, 0.01f); // zero turbulence tends to produce a lot of NaNs.
                sigma_u = sigma_w / Mathf.Pow(0.177f + 0.000823f*h, 0.4f);
                sigma_v = sigma_u;
            }
            else
            {
                float highalt_turbulence;
                if (turbulence_severity < 6f)
                {
                    highalt_turbulence = LightTurbulence.Evaluate(h);
                }
                else if (turbulence_severity < 12f)
                {
                    highalt_turbulence = Mathf.Lerp(LightTurbulence.Evaluate(h), ModerateTurbulence.Evaluate(h), (turbulence_severity - 6f) / 6f);
                }
                else if (turbulence_severity < 18f)
                {
                    highalt_turbulence = Mathf.Lerp(ModerateTurbulence.Evaluate(h), SevereTurbulence.Evaluate(h), (turbulence_severity - 12f) / 6f);
                }
                else
                {
                    highalt_turbulence = SevereTurbulence.Evaluate(h);
                }
                highalt_turbulence /= M_TO_FEET;

                if (altitude_above_ground >= HIGH_ALTITUDE_THRESHOLD)
                {
                    turbulence_severity = highalt_turbulence;
                }
                else
                {
                    turbulence_severity = Mathf.Lerp(turbulence_severity, highalt_turbulence, (HIGH_ALTITUDE_THRESHOLD - altitude_above_ground) / (HIGH_ALTITUDE_THRESHOLD - LOW_ALTITUDE_THRESHOLD));
                }

                Lu = LENGTH_SCALE_THOUSANDFT;
                Lv = Lw = 0.5f * Lu;
                sigma_u = Mathf.Max(turbulence_severity, 0.01f);
                sigma_v = sigma_w = sigma_u;
            }

            turbSev = turbulence_severity;
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
            ComputeProcessParameters(altitude, altitude_above_ground, wind_velocity.magnitude);
            float tas = (wind_velocity + vehicle_velocity).magnitude; // the true air speed
            float T = dt * tas; // distance traveled within the frozen turbulence field

            // Slight fudge for slow moving craft - limit T such that T / L is always at least 0.1 to avoid "sticky" state
            // Because the evolution of Y_v_state etc is proportional to L/(T+L) for very small T this evolution can become
            // too small resulting in excessive gusts at low speed if the craft previously travelled at high speed (e.g. reentry).
            T = Mathf.Max(T, Lv * 0.1f);

            Y_u = ComputeProcess(T, Lu, Y_u);
            Y_v = ComputeLateralProcess(T, Lv, Y_v, ref Y_v_state);
            Y_w = ComputeLateralProcess(T, Lw, Y_w, ref Y_w_state);

            float avg_sigma_u = ComputeAveragedSigma(sigma_u, T, Lu);
            float avg_sigma_v = ComputeAveragedSigma(sigma_v, T, Lv);
            float avg_sigma_w = ComputeAveragedSigma(sigma_w, T, Lw);

            output.x = Y_u*avg_sigma_u;
            output.y = Y_v*avg_sigma_v;
            output.z = Y_w*avg_sigma_w;

            //object[] args = { T, Y_u, Y_v, Y_w, sigma_u, sigma_v, sigma_w, altitude_above_ground, Lu, Lv, Lw };
            //Debug.Log(String.Format("Gusts Model: alt = {7}  T = {0}\n   Y_u={1}\n   Y_v={2}\n   Y_w={3}\n   sigma_u={4}  sigma_v={5}  sigma_w={6}\n   Lu={8}  Lv={9}  Lw={10}\n", args));
        }
    };


    [KSPAddon(KSPAddon.Startup.FlightAndKSC, false)]
    public class KerbalWind : DaMichelToolbarSuperWrapper.PluginWithToolbarSupport
    {
        // main window
        Rect        windowRect = new Rect(100,100,-1,-1);
        bool        isWindowOpen = true;
        // gui stuff
        float       medianWindSpdGuiState = 0.0f; // the value that is being increased when you hit the +/- buttons
        float       medianWindSpd = 0.0f;
        string      windSpdLabel = "";
        float       gustDurationGuiState = 2f;
        string      gustDurationLabel = "";
        float       gustStrengthGuiState = 2f;
        string      gustStrengthLabel = "";
        string      windDirLabel = "x";
        string      windowTitle  = "";
        bool        needsUpdate = true;
        GUISkin     skin;
        // Weather
        OpenSimplex2S simplexNoise;
        float currentWindSpeed;
        float oldWindSpeed;
        float newWindSpeed;
        float currentWindDir;
        float oldWindDir;
        float newWindDir;
        double blendStart;
        double blendDuration;
        float weatherLat = -1f;
        float weatherLng = -1f;
        float weatherTime = 0f;
        // wind
        float altitudeMul = 0f;
        Vector3 windVectorWS; // final wind direction and magnitude in world space
        Vector3 windVector; // wind in "map" space, y = north, x = east (?)
        Vector3 windDirection;
        bool    windEnabled = false;
        double debugUT = 0f;
        // gusts
        Vector3 oldGustsVectorWS;
        Vector3 currentGustsVectorWS;
        Vector3 newGustsVectorWS;
        double gustStart;
        double gustDuration;
        double gustBlendStart;
        double gustBlendDuration;
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
                visibleInScenes = new GameScenes[] { GameScenes.FLIGHT, GameScenes.SPACECENTER }
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

            simplexNoise = new OpenSimplex2S(HighLogic.CurrentGame.Seed);

            currentWindSpeed = oldWindSpeed = newWindSpeed = 0f;
            currentWindDir = oldWindDir = newWindDir = -1f;
            blendStart = 0;
            blendDuration = 0;
            weatherLat = -1000f;
            weatherLng = -1000f;
            weatherTime = -1f;

            CalculateWeather();
            gustsmodel.Init();
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
            settings.AddValue("windSpdGuiState", medianWindSpdGuiState);
            settings.AddValue("gustDurationGuiState", gustDurationGuiState);
            settings.AddValue("gustStrengthGuiState", gustStrengthGuiState);
            settings.Save(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(KerbalWind)) + "/settings.cfg");
        }


        void LoadSettings()
        {
            ConfigNode settings = ConfigNode.Load(AssemblyLoader.loadedAssemblies.GetPathByType(typeof(KerbalWind)) + "/settings.cfg");
            if (settings != null)
            {
                float x = windowRect.xMin, y = windowRect.yMin; // making structs immutable sure was a good idea ...
                Util.TryReadValue(ref x, settings, "windowRect.xMin");
                Util.TryReadValue(ref y, settings, "windowRect.yMin");
                windowRect = new Rect(x, y, windowRect.width, windowRect.height); // it's so much safer and reduces the amount of awkward code one has to write ... 
                LoadMutableToolbarSettings(settings);
                LoadImmutableToolbarSettings(settings);
                Util.TryReadValue(ref medianWindSpdGuiState, settings, "windSpdGuiState");
                Util.TryReadValue(ref gustDurationGuiState, settings, "gustDurationGuiState");
                Util.TryReadValue(ref gustStrengthGuiState, settings, "gustStrengthGuiState");
            }
            medianWindSpd = Util.Floor(medianWindSpdGuiState, 1); // round to 1 d.p. so we go relatively slowly in 0.1m/s steps while the MB is down.
            windSpdLabel = medianWindSpd.ToString("F1");
            gustDurationLabel = gustDurationGuiState.ToString("F1");
            gustStrengthLabel = gustStrengthGuiState.ToString("F1");
        }
        #endregion

        bool UpdateCoordinateFrame()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                mSurfaceToWorld = Matrix4x4.identity;
                return false;
            }
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
            if (CalculateWeather())
            {
                ComputeWindVector();
            }

            if (!HighLogic.LoadedSceneIsFlight)
                return;

            if (windEnabled && currentWindSpeed > 0f && medianWindSpd >= 0.1f)
            {
                UpdateCoordinateFrame();
                windVectorWS = mSurfaceToWorld * windVector;
                if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null)
                {
                    Vessel vessel = FlightGlobals.ActiveVessel;

                    if (FlightGlobals.ActiveVessel.atmDensity <= 0d)
                    {
                        windVectorWS = Vector3.zero;
                        currentGustsVectorWS = Vector3.zero;
                    }
                    else
                    {
                        double radarAltitude = vessel.altitude - Math.Max(0, vessel.terrainAltitude); // terrainAltitude is the deviation of the terrain from the sea level.
                        altitudeMul = gustsmodel.altMultiplier((float)radarAltitude);
                        windVectorWS *= altitudeMul;

                        double UT = Planetarium.GetUniversalTime();
                        gustStart = Math.Min(gustStart, UT);
                        if ((UT - gustStart) >= gustDuration)
                        {
                            // New gust
                            float dt = Time.deltaTime;
                            Vector3 vehicle_velocity = vessel.srf_velocity;
                            gustsmodel.Update(dt, windVectorWS, vehicle_velocity, (float)vessel.altitude, (float)radarAltitude);
                            Vector3 Yuvw = gustsmodel.gustMagnitude;
                            newGustsVectorWS = Yuvw[0] * windDirection;
                            newGustsVectorWS += Yuvw[2] * vessel.upAxis;
                            newGustsVectorWS += Yuvw[1] * Vector3.Cross(windDirection, vessel.upAxis);
                            newGustsVectorWS *= Mathf.Max(0.5f, gustStrengthGuiState);
                            oldGustsVectorWS = currentGustsVectorWS;

                            // 25% variance on duration
                            gustDuration = gustDurationGuiState + gustsmodel.randGauss() * gustDurationGuiState * 0.25;
                            // blend is approxiamtely 10% of of gust
                            gustBlendDuration = (gustDurationGuiState + gustsmodel.randGauss() * gustDurationGuiState * 0.25) * 0.1;

                            // Gusts are shorter when the vessel is moving quickly (because you're flying through different air flows)
                            double speedFactor = Mathf.Clamp(100.0f / vehicle_velocity.magnitude, 0.05f, 2f);
                            gustDuration *= speedFactor;
                            gustBlendDuration *= speedFactor;

                            gustStart = UT;
                            gustBlendStart = UT;
                        }

                        if (gustBlendDuration > 0d)
                        {
                            gustBlendStart = Math.Min(gustBlendStart, UT);
                            float t = (float)((UT - gustBlendStart) / gustBlendDuration);
                            t = Mathf.Clamp01(t);
                            currentGustsVectorWS = oldGustsVectorWS + (newGustsVectorWS - oldGustsVectorWS) * t;
                            if (t >= 1f)
                                gustBlendDuration = 0f;
                        }
                    }
                }
                else
                {
                    currentGustsVectorWS = Vector3.zero;
                    altitudeMul = 1f;
                }
            }
            else
            {
                windVectorWS = Vector3.zero;
                currentGustsVectorWS = Vector3.zero;
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
                    return (windVectorWS + currentGustsVectorWS);
                else
                    return windVectorWS;
            }
        }

        // Summed simplex noise across multiple octaves
        private double CalculateSimplexNoise(double x, double y, double z, uint octaves, double persistance, double lacunarity)
        {
            double noise = 0d;
            double maxAmp = 0.5;
            double amp = 1d;

            for (int i = 0; i < octaves; ++i)
            {
                noise += simplexNoise.Noise3_XYBeforeZ(x, y, z) * amp;
                maxAmp += amp * 0.5;    // This gives a slightly wider spread, which avoids multi-octave noise tending towards the centre excessively.
                x *= lacunarity;
                y *= lacunarity;
                z *= lacunarity;
                amp *= persistance;
            }

            return noise / maxAmp;
        }

        public static PQSCity FindKSC(CelestialBody home)
        {
            if (home != null)
            {
                if (home.pqsController != null && home.pqsController.transform != null)
                {
                    Transform t = home.pqsController.transform.Find("KSC");
                    if (t != null)
                    {
                        PQSCity KSC = (PQSCity)t.GetComponent(typeof(PQSCity));
                        if (KSC != null) { return KSC; }
                    }
                }
            }

            PQSCity[] cities = Resources.FindObjectsOfTypeAll<PQSCity>();
            foreach (PQSCity c in cities)
            {
                if (c.name == "KSC")
                {
                    return c;
                }
            }

            return null;
        }

        public bool CalculateWeather()
        {
            float lat = float.NaN;
            float lng = float.NaN;
            float latlngStep = 0.25f;

            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.ActiveVessel != null &&
                FlightGlobals.ActiveVessel.mainBody == FlightGlobals.GetHomeBody())
            {
                Vessel vessel = FlightGlobals.ActiveVessel;
                lat = Mathf.Floor((float)vessel.latitude / latlngStep) * latlngStep;
                lng = Mathf.Floor((float)vessel.longitude / latlngStep) * latlngStep;
            }
            else if (SpaceCenter.Instance != null)
            {
                PQSCity ksc = FindKSC(FlightGlobals.GetHomeBody());
                if (ksc)
                {
                    lat = Mathf.Floor((float)ksc.lat / latlngStep) * latlngStep;
                    lng = Mathf.Floor((float)ksc.lon / latlngStep) * latlngStep;
                }
                else
                {
                    lat = Mathf.Floor((float)SpaceCenter.Instance.Latitude / latlngStep) * latlngStep;
                    lng = Mathf.Floor((float)SpaceCenter.Instance.Longitude / latlngStep) * latlngStep;
                }
            }

            if (!float.IsNaN(lat) && !float.IsNaN(lng))
            {
                double UT = Planetarium.GetUniversalTime();
                int gameTime = (int)(UT / 1200d);

                debugUT = UT;

                bool update = needsUpdate || (gameTime != weatherTime) || (lat != weatherLat) || (lng != weatherLng);
                if (update)
                {
                    medianWindSpd = Util.Floor(medianWindSpdGuiState, 1); // round to 1 d.p. so we go relatively slowly in 0.1m/s steps while the MB is down.
                    blendStart = UT;
                    if (HighLogic.LoadedSceneIsFlight && currentWindDir >= 0f)
                        blendDuration = 5d;
                    weatherLat = lat;
                    weatherLng = lng;
                    weatherTime = gameTime;
                    needsUpdate = false;

                    if (medianWindSpd < 0.1f)
                    {
                        windEnabled = false;
                        return false;
                    }

                    // This controls how dramatically the wind changes
                    float noiseTime = weatherTime * 0.002f;

                    // simplex noise generated speed 3 octaves respectively.
                    const float speedFreq = 0.1f;
                    float speedSample = (float)CalculateSimplexNoise(weatherLat * speedFreq, weatherLng * speedFreq, noiseTime, 3, 0.25, 8d);
                    speedSample = speedSample * 0.5f + 0.5f;
                    // Because the distribution used is not bounded on the upper end, this needs to be clamped to avoid NaNs.
                    speedSample = Mathf.Clamp(speedSample, 0f, 0.9999f);

                    // Wind speeds are a Rayleigh distribution with user specified median
                    float sigmasq = -(medianWindSpd * medianWindSpd) / (2 * Mathf.Log(0.5f));
                    newWindSpeed = Mathf.Sqrt(-2f * sigmasq * Mathf.Log(1f - speedSample));

                    // direction is generated from the curl of the noise field, using the finite differences method.
                    const float dirFreq = 0.05f;
                    const float epsilon = dirFreq * 0.01f;
                    Vector2 dir = new Vector2((float)CalculateSimplexNoise(weatherLat * dirFreq + 10f, weatherLng * dirFreq - epsilon, noiseTime, 2, 0.2, 8d) -
                        (float)CalculateSimplexNoise(weatherLat * dirFreq + 10f, weatherLng * dirFreq + epsilon, noiseTime, 2, 0.2, 8d),
                        (float)CalculateSimplexNoise(weatherLat * dirFreq + 10f + epsilon, weatherLng * dirFreq, noiseTime, 2, 0.2, 8d) -
                        (float)CalculateSimplexNoise(weatherLat * dirFreq + 10f - epsilon, weatherLng * dirFreq, noiseTime, 2, 0.2, 8d));
                    dir.Normalize();

                    // Direction is just the direction the vector points in.
                    newWindDir = Mathf.Acos(dir.y) * Mathf.Rad2Deg;
                    if (dir.x < 0)
                        newWindDir = 360f - newWindDir;

                    oldWindSpeed = currentWindSpeed;
                    oldWindDir = currentWindDir;

                    // Snap to settings in the space centre.
                    if (blendDuration <= 0f)
                    {
                        currentWindSpeed = newWindSpeed;
                        currentWindDir = newWindDir;
                        if (currentWindDir > 360f)
                            currentWindDir -= 360f;
                        if (currentWindDir < 0f)
                            currentWindDir += 360f;
                    }
                }

                if (blendDuration > 0f)
                {
                    blendStart = Math.Min(blendStart, UT);
                    float t = (float)((UT - blendStart) / blendDuration);
                    currentWindSpeed = Mathf.Lerp(oldWindSpeed, newWindSpeed, t);
                    currentWindDir = Mathf.LerpAngle(oldWindDir, newWindDir, t);
                    if (currentWindDir > 360f)
                        currentWindDir -= 360f;
                    if (currentWindDir < 0f)
                        currentWindDir += 360f;
                    if (t >= 1f)
                        blendDuration = 0f;
                    update = true;
                }

                return update;
            }
            else
            {
                debugUT = -1f;
                windEnabled = false;
                return false;
            }
        }

        void ComputeWindVector()
        {
            // X = north, consistent with vessel axes where x points north for vessels on the KSC runway pointing east
            // Z = east
            windDirection = new Vector3(-Mathf.Cos(currentWindDir * Mathf.Deg2Rad), 0f, -Mathf.Sin(currentWindDir * Mathf.Deg2Rad));
            windEnabled  = true;
            windVector = currentWindSpeed * windDirection;
            gustsmodel.Init();

            string WindLabelNS = (windDirection.x < 0f) ? "N" : "S";
            string windLabelEW = (windDirection.z < 0f) ? "E" : "W";

            float absDirX = Mathf.Abs(windDirection.x);
            if (absDirX >= Mathf.Cos(11.25f * Mathf.Deg2Rad))
                windDirLabel = WindLabelNS;
            else if (absDirX >= Mathf.Cos(33.75f * Mathf.Deg2Rad))
                windDirLabel = WindLabelNS + WindLabelNS + windLabelEW;
            else if (absDirX >= Mathf.Cos(56.25f * Mathf.Deg2Rad))
                windDirLabel = WindLabelNS + windLabelEW;
            else if (absDirX >= Mathf.Cos(78.75f * Mathf.Deg2Rad))
                windDirLabel = windLabelEW + WindLabelNS + windLabelEW;
            else
                windDirLabel = windLabelEW;
        }


        void OnGUI()
        {
            if (isWindowOpen && buttonVisible)
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

                Vector3 totalWindWS = windVectorWS + currentGustsVectorWS;

                if (HighLogic.LoadedSceneIsFlight)
                    windowTitle = "Wind: " + totalWindWS.magnitude.ToString("F1") + " m/s";
                else
                    windowTitle = "Kerbal Wind";

                GUILayout.Space(4);
                GUILayout.Label("Median Speed", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    MakeNumberEditField(ref windSpdLabel, ref medianWindSpdGuiState, ref needsUpdate, 1);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label("Gust Duration", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    bool gustUpdate = true;
                    MakeNumberEditField(ref gustDurationLabel, ref gustDurationGuiState, ref gustUpdate, 1);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label("Gust Strength", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    MakeNumberEditField(ref gustStrengthLabel, ref gustStrengthGuiState, ref gustUpdate, 1);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                    GUILayout.Label("Weather", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    GUILayout.Box(windEnabled ? $"{currentWindSpeed:F1} m/s" : "None", GUILayout.MinWidth(80));
                    GUILayout.Box(windEnabled ? $"{currentWindDir:F0}° {windDirLabel}" : "", GUILayout.MinWidth(80));
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                if (currentWindSpeed <= 0.5f)
                    GUILayout.Box("<color=#ffffff>Calm</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 1.5f)
                    GUILayout.Box("<color=#aef1f9>Light air</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 3.3f)
                    GUILayout.Box("<color=#96f7dc>Light breeze</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 5.5f)
                    GUILayout.Box("<color=#96f7b4>Gentle breeze</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 7.9f)
                    GUILayout.Box("<color=#6ff46f>Moderate breeze</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 10.7f)
                    GUILayout.Box("<color=#73ed12>Fresh breeze</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 13.8f)
                    GUILayout.Box("<color=#a4ed12>Strong breeze</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 17.1f)
                    GUILayout.Box("<color=#daed12>High wind</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 20.7f)
                    GUILayout.Box("<color=#edc212>Gale</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 24.4f)
                    GUILayout.Box("<color=#ed8f12>Strong Gale</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed <= 28.4f)
                    GUILayout.Box("<color=#ed6312>Storm</color>", GUILayout.ExpandWidth(true));
                else
                    GUILayout.Box("<color=#ed2912>Violent Storm</color>", GUILayout.ExpandWidth(true));

                GUILayout.Space(2);
                if (currentWindSpeed < 8f)
                    GUILayout.Box("<color=#00e000>Light turbulence</color>", GUILayout.ExpandWidth(true));
                else if (currentWindSpeed < 15f)
                    GUILayout.Box("<color=#e0e000>Moderate turbulence</color>", GUILayout.ExpandWidth(true));
                else
                    GUILayout.Box("<color=#e07000>Severe turbulence</color>", GUILayout.ExpandWidth(true));

#if false
            GUILayout.Space(4);
                GUILayout.Label("WindVec", GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                    GUILayout.Box(windDirection.x.ToString("F3"), GUILayout.MinWidth(40));
                    GUILayout.Box(windDirection.y.ToString("F3"), GUILayout.MinWidth(40));
                    GUILayout.Box(windDirection.z.ToString("F3"), GUILayout.MinWidth(40));
                GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label("Speed", GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Box(oldWindSpeed.ToString("F2"), GUILayout.MinWidth(40));
            GUILayout.Box(currentWindSpeed.ToString("F2"), GUILayout.MinWidth(40));
            GUILayout.Box(newWindSpeed.ToString("F2"), GUILayout.MinWidth(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label("Dir", GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Box(oldWindDir.ToString("F1"), GUILayout.MinWidth(40));
            GUILayout.Box(currentWindDir.ToString("F1"), GUILayout.MinWidth(40));
            GUILayout.Box(newWindDir.ToString("F1"), GUILayout.MinWidth(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label("Base / Gust", GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Box(windVectorWS.magnitude.ToString("F2"), GUILayout.MinWidth(60));
            GUILayout.Box(currentGustsVectorWS.magnitude.ToString("F2"), GUILayout.MinWidth(60));
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label("Alt / Turb", GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Box(altitudeMul.ToString("F2"), GUILayout.MinWidth(60));
            GUILayout.Box(gustsmodel.turbulenceSev.ToString("F3"), GUILayout.MinWidth(60));
            GUILayout.EndHorizontal();
#endif
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
