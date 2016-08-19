# -*- coding: utf-8 -*-
"""
Created on Mon May 30 17:29:28 2016

@author: Michael

How does gaussian white noise behave when it is input to a running average filter,
i.e. a perfect low pass filter (except for discretization errors)?

I assume ergodicity, i.e. marginal probability densities can 
be estimated by averaging over time. Pretty sure that assumption is justified.
"""

import numpy as np
import scipy.fftpack

import matplotlib.pyplot as pyplot


def gaussian(x, m, sigma):
  return 1./np.sqrt(2 * np.pi)/sigma * np.exp(-np.square((x - m)/sigma)*0.5)

NG = 1024 * 10
gauss_noise_iid = np.random.normal(0., 1., NG)
gauss_fft = scipy.fftpack.fft(gauss_noise_iid)

sigmas = []
cutoffs = []
for cutoff in [1,2,3,4,5,6,7,8,9, 9.5, 9.9]:
  NFILT = int(512 * cutoff)
  RELATIVE_FREQ_LIMIT = 1.*(NG/2-1-NFILT) / (NG/2-1)
  gauss_fft_limited = gauss_fft.copy()
  gauss_fft_limited[NG/2-1-NFILT:NG/2+NFILT] = 0.
  gauss_limited = scipy.fftpack.ifft(gauss_fft_limited).real
  cutoffs.append(RELATIVE_FREQ_LIMIT)
  sigmas.append(np.std(gauss_limited))
  # low pass frequency limit = 6. / 8.
  # correlation length = 8. / 6.
  # -> sigma_limit = sigma * sqrt(6. / 8.)
  
  print 'cutoff %s, sigma: white %s, limited %s' % (RELATIVE_FREQ_LIMIT, np.std(gauss_noise_iid), np.std(gauss_limited))
  if 0:
    pyplot.subplot(4, 1, 1)
    pyplot.plot(gauss_noise_iid)
    pyplot.plot(gauss_limited)
    pyplot.gca().set_ylim((-4., 4.))
    pyplot.subplot(4, 1, 2)
    pyplot.plot(np.abs(gauss_fft))
    pyplot.plot(np.abs(gauss_fft_limited))
    pyplot.subplot(4, 1, 3)
    y = np.linspace(-4., 4., 128)
    h1 = gaussian(y, 0., 1.)
    h2 = gaussian(y, 0., 0.44)
    pyplot.hist(gauss_noise_iid, bins = 32, range = (-4., 4.), histtype='step', normed = True)
    pyplot.hist(gauss_limited, bins = 32, range = (-4., 4.), histtype='step', normed = True)
    pyplot.plot(y, h1)
    pyplot.plot(y, h2)
    pyplot.subplot(4, 1, 4)
    pyplot.show()

T = np.reciprocal(np.asarray(cutoffs, dtype = np.float))
print T
pyplot.plot(T, sigmas)
pyplot.gca().set(xscale = 'log')
x = np.logspace(0, 2, 1024)
pyplot.plot(x, np.sqrt(1./x), color = 'red')
pyplot.show()
# The curves should agree very well.
# This means that the variance of the smoothed signal decreases as T0/T, where
# T0 is the correlation length of the input signal and T the width of the 
# moving average filter.

# What happens when T < T0? Or in other words when the low pass filter 
# corresponding to the moving average over T comprises all frequencies of the 
# input. Then of course the signal is preserved. In particular, sigma 
# is the same.

# How about super sampling a signal at distances smaller than its correlation
# length? Seems to preserve gaussian processes. Seems intuitive that it
# cannot change the (marginal) distribution of the input.