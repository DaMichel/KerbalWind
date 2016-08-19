# -*- coding: utf-8 -*-
"""
Created on Fri May 27 21:14:27 2016

@author: Michael
"""

import numpy as np
from scipy.fftpack import fft, ifft, fftfreq, fftshift
from numpy import pi

import matplotlib.pyplot as pyplot

L = 0.01
N = 800 # number of points
T = 1.0 / 200.0  # point spacing 
x = np.linspace(-N*T/2., (N-1)*T/2., N) # real space sampling points
y = np.exp(-np.abs(x/L)) 
print 'max frequency: %s' % (1./2/T)
print 'y sum = %s, y avg = %s' % (np.sum(y), np.sum(y)/N)

yf = fft(y)
xf = fftfreq(N, T) # sampling points in frequency domain. First entry corresponds to f=0, then come frequencies in increasing order until the middle of the array. From there on negative frequencies are arranged in decreasing order, i.e. the most negative frequency comes first.
xf = fftshift(xf) # rearrange array entries suitable for display

pyplot.subplot(5, 1, 1)
pyplot.plot(x, y)
pyplot.grid()

# for non-periodic function we have to scale with T because Dirac Comb and because FFT assumes periodicity!
pyplot.subplot(5, 1, 2)
exact = 2.*L / (1 + 4*pi*pi*xf*xf*L*L)
pyplot.plot(xf, T*fftshift(np.abs(yf))) 
pyplot.plot(xf, exact, label = 'exact')
pyplot.legend()
pyplot.grid()

pyplot.subplot(5, 1, 3)
y2f = 2.*L/pi / (1 + np.square(fftfreq(N,T))*L*L)
y2  = 1./(T)*fftshift(ifft(y2f))
y2exact = 2.*np.exp(-2.*pi*np.abs(x/L)) 
pyplot.plot(x, y2)
pyplot.plot(x, y2exact, label = 'exact')
pyplot.legend()
pyplot.grid()

pyplot.subplot(5, 1, 4)
y3f = 2.*L / (1 + np.square(pi*2.*fftfreq(N,T))*L*L)
y3  = 1./(T)*fftshift(ifft(y3f))
y3exact = np.exp(-np.abs(x/L)) 
pyplot.plot(x, y3)
pyplot.plot(x, y3exact, label = 'exact')
pyplot.legend()

pyplot.subplot(5, 1, 5)
ysin = np.sin(2. * 2.0*pi*x) + 0.2*np.sin(90.5 * 2.0*pi*x) # there is a factor 1/2 in the result because the amplitude contribution is distributed between the positive and negative frequency component!
yfsin = fft(ysin)
pyplot.plot(xf, 1./N*fftshift(np.abs(yfsin)))
pyplot.show()