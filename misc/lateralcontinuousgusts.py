# -*- coding: utf-8 -*-
"""
Created on Wed May 25 17:25:55 2016

@author: Michael
"""

import numpy as np
from scipy.fftpack import fft, ifft, fftfreq, fftshift
import math
import matplotlib.pyplot as pyplot


def drydenNormalizedSpectralPowerDensity(omega, (L,)):
  return 2. * L * (1 + 12 * (L*omega*2.*np.pi)**2) / (1. +4.*(L*omega*2.*np.pi)**2)**2

L = 300  # meters
N = 1024 * 10
white_noise = np.random.normal(0., 1., N)

def lateral_filter3(X, T_):
  Y = np.zeros_like(X)
#Pretty Exact, theoretically, however horrible because unstable for small sampling distances T!!!!
#I gave it a try, but it is no good.
#  for n in xrange(len(X)):
#    C1 = math.exp(-0.5*T/L)
#    SQRT3 = math.sqrt(3)
#    B1 = -2*SQRT3*L*(C1-1)**2
#    B2 = (2*SQRT3*L*C1 - T*C1 + T*C1*SQRT3)*(C1 - 1)**2
#    C2 = (-2*SQRT3*L + 2 *SQRT3 *L * C1 - T*C1 + T*C1*SQRT3)
#    A0 = C2
#    A1 = -2*C1*C2
#    A2 = C1*C1*C2
#    C3 = np.sqrt(2.*L/T) #np.sqrt(2.*L)/T
#    Y[n] = (C3*B1 * X[n] + C3*B2 * X[n-1] - A1 * Y[n-1] - A2 * Y[n-2]) / A0
  
  # Awesome! This seems to work!
  L1 = 1.2*L # modified length scale
  M1 = 1 # how often to chain
  F1 = 9./8. # mixing factor
  L2 = 3.*L
  M2 = 2
  F2 = -1./8.
  N1 = math.pow(2*L1, 1./2) # normalization factor
  N2 = math.pow(2*L2, 1./4)
  b1 = T/(T+L1)
  a1 = L1/(T+L1)
  b2 = T/(T+L2)
  a2 = L2/(T+L2)
  Y = np.zeros_like(X)
  TMP1 = np.zeros((M1+1, len(X)))
  TMP2 = np.zeros((M2+1, len(X)))
  TMP1[0,:] = X
  TMP2[0,:] = X
  for n in xrange(len(X)):
    for j in xrange(0, M1):
      TMP1[j+1, n] = TMP1[j, n]*b1*N1 + a1*TMP1[j+1, n-1]
    for j in xrange(0, M2):    
      TMP2[j+1, n] = TMP2[j, n]*b2*N2 + a2*TMP2[j+1, n-1]
    Y[n] = F1 * TMP1[M1,n] + F2 * TMP2[M2,n]
  Y *= 1./math.sqrt(T)
  return Y

T = 100. / 30.  # m/s * frames per second -> sample distance
gusts = lateral_filter3(white_noise, T)
gusts = gusts
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

M = 2000
corr = autocorrelation(gusts, M/2, 100)
spectrum = T*fft(corr)
omegas = fftfreq(M, T)
positions = np.linspace(-T*M/2, T*(M-1)/2, M)
drydenSpectrum = drydenNormalizedSpectralPowerDensity(omegas, (L,))
drydenBacktransform = 1./T*ifft(drydenSpectrum)

pyplot.subplot(3, 1, 1)
pyplot.plot(np.linspace(0., T*N, N), gusts)
# well F... proper magnitude scaling  ...
pyplot.subplot(3, 1, 2)
pyplot.plot(positions, corr)
corr_exact = 0.25*np.exp(-0.5*np.abs(positions)/L)*(2.*math.sqrt(3)/L - np.abs(positions)*(math.sqrt(3)-1)/L/L)
pyplot.plot(positions, corr_exact)
pyplot.plot(positions, fftshift(drydenBacktransform))

pyplot.subplot(3, 1, 3)
pyplot.plot(fftshift(omegas), fftshift(np.abs(spectrum)))
pyplot.plot(fftshift(omegas), fftshift(drydenSpectrum))
test = T*fft(corr_exact)
pyplot.plot(fftshift(omegas), fftshift(np.abs(test)))
pyplot.show()