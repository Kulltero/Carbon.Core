﻿using System;
using UnityEngine;

/*
 *
 * Copyright (c) 2022-2023 Carbon Community 
 * All rights reserved.
 *
 */

namespace Carbon.Components;

public struct TimeMeasure : IDisposable
{
#if DEBUG
	internal string _name;
	internal string _warn;
	internal int _miliseconds;
	internal int _timeSince;
#endif

	public static TimeMeasure New(string name, int miliseconds = 75, string warn = null)
	{
#if DEBUG
		var result = default(TimeMeasure);
		result._name = name;
		result._warn = warn;
		result._miliseconds = miliseconds;
		result._timeSince = Environment.TickCount;
		return result;
#else
		return default;
#endif
	}

	public void Dispose()
	{
#if DEBUG
		var num = Environment.TickCount;

		if (Mathf.Abs(Environment.TickCount - num) >= _miliseconds)
		{
			Carbon.Logger.Warn(
				$" {_name} took {num:0}ms [abv {_miliseconds}]{(string.IsNullOrEmpty(_warn) ? "" : (": " + _warn))}");
		}
#endif
	}
}
