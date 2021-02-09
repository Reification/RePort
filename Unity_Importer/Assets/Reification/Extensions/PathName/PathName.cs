// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	/// <summary>
	/// Path of GameObject in Scene
	/// </summary>
	[Serializable]
	public class PathName : ICloneable {
		[Serializable]
		public class PathStep : ICloneable {
			public string name; // Name of path entry
			public enum StepType {
				Path, // Can be partitioned when matching
				Name // Must exctly match the name of an object
			}
			public StepType type; // Type of path entry

			/// <returns>Deep-copy yielding a independent instance of name</returns>
			public object Clone() {
				var step = new PathStep();
				step.type = type;
				step.name = name.Clone() as string;
				return step;
			}
		}
		public List<PathStep> path;
		// QUESTION: Should this be an array?
		// QUESTION: Should PathStep and path be protected?
		// GUESS: It is expected to be invariant, so direct access & list manipulation are not needed

		/// <summary>
		/// Empty path identifies world
		/// </summary>
		public PathName() {
			path = new List<PathStep>();
		}

		/// <summary>
		/// Constructor for object path
		/// </summary>
		public PathName(GameObject gameObject) {
			path = new List<PathStep>();
			Transform parent = gameObject.transform;
			while(parent) {
				var step = new PathStep();
				step.name = parent.name;
				step.type = PathStep.StepType.Name;
				path.Insert(0, step);
				parent = parent.parent;
			}
		}

		/// <summary>
		/// Constructor for single object name, or for object path
		/// </summary>
		/// <remarks>
		/// Unity object naming is unrestricted. In particular:
		/// - Names can be repeated.
		/// - Names can include the path separator character '/'.
		/// - Names can be empty.
		/// Consequently, a PathName is not guaranteed to uniquely identify a GameObject
		/// Paths are relative to a specified GameObject, so the name
		/// argument must begin and end with GameObject names.
		/// - name = "" matches "" which is a root GameObject
		/// - name = "/" matches "" which is a child of ""
		/// - name = "/" also matches "/" which is a root GameObject
		/// </remarks>
		public PathName(string name, PathStep.StepType type = PathStep.StepType.Path) {
			var step = new PathStep();
			step.name = name;
			step.type = type;
			path = new List<PathStep>();
			path.Add(step);
		}

		/// <returns>deep-copy yielding an independent instance of path</returns>
		public object Clone() {
			var pathName = new PathName();
			pathName.path = new List<PathStep>();
			for(int p = 0; p < path.Count; ++p) pathName.path.Add(path[p].Clone() as PathStep);
			return pathName;
		}

		// PROBLEM: The string notation for empty-name root game objects is ambiguous.
		// QUESTION: Is it possible to coerce ToString() into a canonical form by using the name/path
		// distinction?

		/// <returns>The path and name of the GameObject</returns>
		/// <remarks>
		/// The path consistent with the definition of GameObject.Find(string)
		/// https://docs.unity3d.com/ScriptReference/GameObject.Find.html
		/// The path consists of every parent in the transform lineage,
		/// with "/" used to separate names, and used to begin paths from
		/// scene root.
		/// </remarks>
		public override string ToString() {
			string pathString = "";
			for(int p = 0; p < path.Count; ++p) {
				pathString += path[p].name;
				if(p + 1 < path.Count) pathString += "/";
			}
			if(pathString.Length == 0 || pathString[0] != '/') pathString = "/" + pathString;
			return pathString;
		}

		public static implicit operator string(PathName pathName) => pathName.ToString();

		public static implicit operator PathName(string name) => new PathName(name);

		/// <summary>
		/// Evaluates a single step of a match.
		/// </summary>
		/// <returns>false when incompatibility is found</returns>
		/// <remarks>
		/// In the case of a match, arguments will be modified according 
		/// to their respective recursion types.
		/// </remarks>
		public static bool MatchStep(PathName lhs, PathName rhs) {
			// Empty path denotes root, so empty paths are equal
			if(lhs.path.Count == 0 && rhs.path.Count == 0) return true;
			if(lhs.path.Count == 0 || rhs.path.Count == 0) return false;

			// Count the matching characters from start of path step name
			int same = 0;
			for(int c = 0; c < lhs.path[0].name.Length && c < rhs.path[0].name.Length; ++c) {
				if(lhs.path[0].name[c] != rhs.path[0].name[c]) break;
				++same;
			}

			switch(lhs.path[0].type) {
			case PathStep.StepType.Name:
				if(same < lhs.path[0].name.Length) return false;
				lhs.path.RemoveAt(0); // Recurse by reduction
				break;
			case PathStep.StepType.Path:
				if(same < lhs.path[0].name.Length) {
					if(lhs.path[0].name[same] != '/') return false;
					lhs.path[0].name = lhs.path[0].name.Substring(same + 1); // Recurse by partition
					break;
				}
				lhs.path.RemoveAt(0); // Recurse by reduction
				break;
			}
			switch(rhs.path[0].type) {
			case PathStep.StepType.Name:
				if(same < rhs.path[0].name.Length) return false;
				rhs.path.RemoveAt(0); // Recurse by reduction
				break;
			case PathStep.StepType.Path:
				if(same < rhs.path[0].name.Length) {
					if(rhs.path[0].name[same] != '/') return false;
					rhs.path[0].name = rhs.path[0].name.Substring(same + 1); // Recurse by partition
					break;
				}
				rhs.path.RemoveAt(0); // Recurse by reduction
				break;
			}

			return true;
		}

		/// <returns>the GameObjects specified by this path relative to parent</returns>
		/// <remarks>
		/// If the path matches no GameObjects the returned array will be empty.
		/// An empty path matches world, which will return { null }
		/// A null parent matches world.
		/// </remarks>
		public Transform[] Find(Transform parent = null) {
			if(path.Count == 0) return new Transform[] { parent };

			List<Transform> findList = new List<Transform>();
			Transform[] childList = TransformExtensions.Children(parent);
			foreach(var childItem in childList) {
				// Evaluate match
				var lhsRecurse = Clone() as PathName;
				var rhsRecurse = new PathName(childItem.name, PathStep.StepType.Name);
				if(!MatchStep(lhsRecurse, rhsRecurse)) continue;
				findList.AddRange(lhsRecurse.Find(childItem));
			}
			return findList.ToArray();
		}


		/// <returns>true when path matches name, which may include '/' characters, and may be empty</returns>
		public static bool operator ==(PathName lhs, PathName rhs) {
			// Empty path denotes root, so empty paths are equal
			if(lhs.path.Count == 0 && rhs.path.Count == 0) return true;
			if(lhs.path.Count == 0 || rhs.path.Count == 0) return false;

			var lhsRecurse = lhs.Clone() as PathName;
			var rhsRecurse = rhs.Clone() as PathName;
			if(!MatchStep(lhsRecurse, rhsRecurse)) return false;
			return lhsRecurse == rhsRecurse;
		}

		public static bool operator !=(PathName lhs, PathName rhs) {
			return !(lhs == rhs);
		}

		public override bool Equals(object obj) {
			if(obj == null) return false;
			var rhs = obj as PathName;
			if(rhs as object == null) return false;
			return this == rhs;
		}

		public override int GetHashCode() {
			return ToString().GetHashCode();
		}
	}
}
