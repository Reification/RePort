using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Collections;
using System.Collections.Generic;
using Codice.Client.BaseCommands;
using UnityEngine;

// TEMP
using UnityEditor;

namespace Reification.CloudTasks {
	public class AWSCloudTasks {
		const string menuItemName = "Reification/TEST AWS Cloud Tasks";
		const int menuItemPriority = 100;

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			var test = new AWSCloudTasks();
		}
		
		
		public const string accountFile = "AWSCloudTasks_account.json";

		// SECURITY: WARNING: This is stored in plain text
		[Serializable]
		private class Account {
			// User Pool username
			public string username;

			// User Pool password
			public string password;
			
			// User Pool app client Id
			public string clientId;
			
			// Identity Pool Id
			public string policyId;
		}

		private Account account = null;
		
		DataContractJsonSerializer accountSerializer = new DataContractJsonSerializer(typeof(Account));

		private void LoadAccount() {
			// FIXME: persistentDataPath is project-specific, but this should be universal
			// NOTE: pDP is base/Company/Project/* so up two levels would work
			var accountPath = Path.Combine(Application.persistentDataPath, accountFile);
			if (!File.Exists(accountPath)) {
				Debug.Log($"AWSCloudTasks missing account file {accountPath}");
				return;
			}
			var accountData = File.OpenRead(accountPath);
			account = accountSerializer.ReadObject(accountData) as Account;
			if (account is null) {
				Debug.Log($"AWSCloudTasks unable to read account file {accountPath}");
			}
		}

		// User Pool authentication result on success
		// NOTE: member names match keys in AWS JSON response
		[Serializable]
		private class Authentication {
			public string AccessToken;

			public string IdToken;

			public string RefreshToken;

			public string TokenType;

			public string ExpiresIn;
		}

		private Authentication authentication = null;
		
		DataContractJsonSerializer authenticationSerializer = new DataContractJsonSerializer(typeof(Authentication));

		private void GetAuthentication() {
			
		}

		private string identity = null;

		// Identity Pool credentials result on success
		// NOTE: member names match keys in AWS JSON response
		[Serializable]
		private class Credentials {
			public string AccessKeyId;

			public string SecretKey;

			public string SessionToken;

			public string Expiration;
		}

		private Credentials credentials = null;
		
		DataContractJsonSerializer credentialsSerializer = new DataContractJsonSerializer(typeof(Credentials));

		private void GetCredentials() {
			
		}

		public bool connected { get; private set; } = false;

		public AWSCloudTasks() {
			LoadAccount();
			if(account == null) return;
			Debug.Log("AWSCloudTasks SUCCESS");
			GetAuthentication();
			if(authentication == null) return;
			GetCredentials();
			if(credentials == null) return;

			// TODO: Load bake and build queues
			// Schedule periodic checking of queues
			// IDEA: Queues should be files in the caches
			// or even retrieved from the cloud service
			
			connected = true;
		}

		// Upload model package for baking
		public void UploadBake() {
			// S3 object upload (multi-part?)
			// Enqueue periodic checking for result
		}

		public void DownloadBake() {
			// S3 object download (multi-part?)
			// Check for exists
			// If exists download
			// After download remove object
			// Clear queue
			// Initiate import

			// NOTE: Downloaded files will persist...
			// Since import might fail, upload and download
			// files should be managed with a cache size
			// and recency prioritization
		}

		// Upload model package for building as bundles
		public void UploadBuild() {
			// NOTE: Application needs to be able to access these builds
		}

		// Configure access to builds
		public void BuildAccess() {
			// Default state is private in which the model can only be accessed by owner.
			
			// Permanent users can be added
			// Users can be granted moderator permissions (owner is a moderator)
			// - Invite users
			// - Convert users to permanent
			// - Revoke user access
			// - Start / stop public access (while moderator is present)
			// - Reset model
			// Owner has additional privileges
			// - Promote user to moderator
			// - View & edit full access list
			// - Create / Update / Remove model
			// NOTE: User invitations can be created using time windows
			// with future start and stop times.
			// NOTE: In the case of timed access it should still be possible
			// to download the model before but NOT after the access window.

			// NOTE: Accessing a model is defined by a downloaded bundle
			// and a server address. The server itself additionally has access
			// to a user configuration.
			// NOTE: Updating a model takes an existing bundle and server,
			// copies them to the access configuration, and removes the old configuration.
			// The server inherits the previous user configuration.
			// IMPORTANT: It must be possible to check if a downloaded model is current.
			
			// IMPORTANT: It must be possible to create a guest list
			// including access times, and to receive a list with access
			// permissions to share with those user.
		}
	}
}
