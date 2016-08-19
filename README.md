Kerbal Wind
========================================
Provides a GUI for wind speed settings and implements a [continuous-gusts](https://en.wikipedia.org/wiki/Continuous_gusts) model.
The settings are wind direction, speed and turbulence magnitude.

Forum Thread: http://forum.kerbalspaceprogram.com/threads/107989

Source Code: https://github.com/DaMichel/KerbalWind

![alt text](https://github.com/DaMichel/KerbalWind/raw/master/misc/kerbalwind.jpg "Screenshot")

##### Dependencies
Required: Ferram Aerospace Research

Optional: Toolbar

##### Authors
DaMichel, silverfox8124

##### On the gusts model
It is a partial implementation of random processes described by 
the [Dryden](https://en.wikipedia.org/wiki/Dryden_Wind_Turbulence_Model) model.
From frame to frame, new random perturbations of the wind velocity
vector are computed. This noise exhibits approximate power spectral densities
as specified by the Dryden model. There are different densities for longitudinal
and transversal directions.

This is implemented by generating white Gaussian noises which are input to
infinite impulse response filters generating the desired signals.
As a result, subsequent signal values are correlated according to
the model. The strength of the correlation depends on the distance 
that the craft traveled during one frame. Care has been taken to make it
work for any finite distance, including zero.

When I wrote 'distance' I actually meant the distance traveled 
relative to the baseline wind velocity w. This is because the 
perturbations are assumed to "move" with the velocity w.
Otherwise, they are frozen in time. Hence, they are formally 
described by a random vector field (u_g, v_g, w_g) depending on
the craft position p and velocity v by (u_g, v_g, w_g)(p + t * (v - w)).
An interesting consequence is that if the craft velocity (ground speed)
exactly matches the wind velocity, the craft will not experience any 
turbulence at all. For planes, this is a reasonable simplification 
because this state won't last for very long in any case. ;-)

The filter for u_g was designed by taking the Z-transform of 
the filter response and matching its Taylor expansion for 
small frequencies with a suitable square root of the desired 
power spectral density. Spectral densities for v_g and w_g 
(lateral components) are only approximated so that I could 
build a stable filter from superposition and chaining
of lower order filters used also for u_g. See maple worksheet 
in `misc/`

A severe limitation is the neglect of spatial variations of the wind velocity around
the craft. All parts of the craft experience the same wind. This means
for instance, that roll inducing forces due to uneven lift are not accounted for.
I hope to correct this eventually.

##### Credits
DaMichel started off with some code from KerbalWeatherSystem courtesy of
SilverFox8124 for which I want to thank him. Also much thanks goes to Ippo343
for writing the FAR patch that lets you add wind, and Ferram4 for
making FAR and accepting all those patches.

#### License
The code is subject to the MIT license (see below). In addition, the
creators of derivative work must give credit to silverfox8124 and DaMichel.

-----------------------------------

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

-----------------------------------

The toolbar icon is subject to the WTFPL (http://www.wtfpl.net/)
