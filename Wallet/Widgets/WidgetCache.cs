﻿using System;
using System.Collections.Generic;
using Gtk;

namespace Wallet
{
	public class WidgetCache
	{
		public delegate T Factory<T>() where T : Widget;

		private IDictionary<Type, Widget> dictionary = new Dictionary<Type, Widget>();

		public T Get<T>(Factory<T> factory) where T : Widget {
			if (!Contains<T>()) {
				Put<T>(factory());
			}

			return Get<T>();
		}

		private bool Contains<T>() {
			return dictionary.ContainsKey (typeof(T));
		}

		private void Put<T>(Widget value) where T : Widget {
			dictionary [typeof(T)] = value;
		}

		private T Get<T>() where T : Widget {
			return (T)dictionary [typeof(T)];
		}

	}
}
