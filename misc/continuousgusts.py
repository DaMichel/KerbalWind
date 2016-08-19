# -*- coding: utf-8 -*-
"""
Created on Wed May 25 17:25:55 2016

@author: Michael
"""

import numpy as np
from scipy.fftpack import fft, ifft, fftfreq, fftshift
import math
import matplotlib.pyplot as pyplot

#def drydenNormalizedSpectralPowerDensity(omega, (L,)):
#  return 2 * L / np.pi / (1 + (L * omega)**2)

def drydenNormalizedSpectralPowerDensity(omega, (L,)):
  return 2. * L / (1. + (L * omega*2.*np.pi)**2)

# to convert process to process depending on time and velocity replace as follows
# T <- |U+V| * dt
# the resulting sample points represent values taken in periods of dt while
# flying at speed V through a frozen noise fields that is advected with the
# mean wind velocity U.
# Or may be even better, instead of U use U+Y_u[n-1] to take into account that
# the local environment is advected with U+Y_u instead of the global average
# wind speed amounting to just U.
# Then we don't run into the silly situation where Y == 0 if U == V.
# Of if U == V then planes won't fly very well anyways ... rockets and helicopters do however.

L = 0.1
N = 1024 * 10
white_noise = np.random.normal(0., 1., N)

def longitudinal_filter1(X, T):
  b = np.sqrt(2.*L/T)*T/(T+L) #/np.pi
  a = L/(T+L)
  Y = np.zeros_like(X)
  for n in xrange(len(X)):
    Y[n] = X[n]*b + a*Y[n-1]  # with periodic boundary conditions (Y[n-1] evaluates to Y[0] for n=0)
  return Y


T = 0.01
gusts = longitudinal_filter1(white_noise, T)
print 'sigma input: %s, sigma output = %s' % (np.std(white_noise), np.std(gusts)) # gives me an output sigma of ca. 0.5 times input sigma

def autocorrelation(X, maxTau, step):
  C = np.zeros(2*maxTau)
  N = len(X)
  M = 0.
  for k in xrange(0,N,step):
    M += 1.
    for tau in xrange(-maxTau, maxTau):
      C[tau+maxTau%(2*maxTau)] += X[k]*X[(k+tau)%N]
  C *= 1./M
  return C

M = 1000
corr = autocorrelation(gusts, M/2, 50)
spectrum = T*fft(corr)
omegas = fftfreq(M, T)
positions = np.linspace(-T*M/2, T*(M-1)/2, M)
drydenSpectrum = drydenNormalizedSpectralPowerDensity(omegas, (L,))
drydenBacktransform = 1./T*ifft(drydenSpectrum)

pyplot.subplot(3, 1, 1)
pyplot.plot(np.linspace(0., T*N, N), gusts)

pyplot.subplot(3, 1, 2)
pyplot.plot(positions, corr)
corr_exact = np.exp(-np.abs(positions)/L)
pyplot.plot(positions, corr_exact)
pyplot.plot(positions, fftshift(drydenBacktransform))

pyplot.subplot(3, 1, 3)
pyplot.plot(fftshift(omegas), fftshift(np.abs(spectrum)))
pyplot.plot(fftshift(omegas), fftshift(drydenSpectrum))
test = T*fft(corr_exact)
pyplot.plot(fftshift(omegas), fftshift(np.abs(test)))
pyplot.show()