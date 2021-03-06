﻿using System;

namespace Infrastructure
{
	public class Singleton<T> where T : class, new()
	{
		private static readonly object _lock = new object();
		private static T _instance = null;

		public static T Instance {
			get {
				lock (_lock)
				{
					_instance = _instance ?? new T();

					return _instance;
				}
			}
		}

		//public static Y GetInstance<Y>() {
		//	return (Y)Convert.ChangeType (Instance, typeof(Y));
		//}
	}

//	public class ValueSingleton<T>
//	{
//		private static T _instance = null;
//
//		public static T Instance {
//			get {
//				if (_instance == null)
//					_instance = default(T);
//
//				return _instance;
//			}
//		}
//	}
}

