using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	public class KeyToggleEnabled: MonoBehaviour {
		/// <summary>
		/// Behaviors controlled by toggle key
		/// </summary>
		/// <remarks>
		/// Behaviors enabled states are cycled independently
		/// relative to their current states.
		/// </remarks>
		public List<Behaviour> targetList;

		/// <summary>
		/// Key pressed to cycle enabled state
		/// </summary>
		public KeyCode toggle = KeyCode.None;

		/// <summary>
		/// Optional key that must be held to cycle enabled state
		/// </summary>
		public KeyCode safety = KeyCode.None;

		void Update() {
			if(safety != KeyCode.None && !Input.GetKey(safety)) return;
			if(!Input.GetKeyDown(toggle)) return;
			foreach(var target in targetList) target.enabled = !target.enabled;
		}
	}
}
