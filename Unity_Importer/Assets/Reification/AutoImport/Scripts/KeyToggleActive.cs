using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	public class KeyToggleActive: MonoBehaviour {
		/// <summary>
		/// GameObjects controlled by toggle key
		/// </summary>
		/// <remarks>
		/// GameObjects active states are cycled independently
		/// relative to their current states.
		/// </remarks>
		public List<GameObject> targetList;

		/// <summary>
		/// Key pressed to cycle active state
		/// </summary>
		public KeyCode toggle = KeyCode.None;

		/// <summary>
		/// Optional key that must be held to cycle active state
		/// </summary>
		public KeyCode safety = KeyCode.None;

		void Update() {
			if(safety != KeyCode.None && !Input.GetKey(safety)) return;
			if(!Input.GetKeyDown(toggle)) return;
			foreach(var target in targetList) target.SetActive(!target.activeSelf);
		}
	}
}
