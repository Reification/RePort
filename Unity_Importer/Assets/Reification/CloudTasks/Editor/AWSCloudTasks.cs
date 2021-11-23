using System;
using System.IO;
using System.Net.Http;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;

// NOTE: Scene bundle access is required at runtime.
// QUESTION: Will an authenticated service be used to fetch bundles?
// OBSERVATION: Another option would be to provide public access to encrypted data
// and then share keys through a side-channel.
// With this option data existence is public, but content is private, so it is not ideal.
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
		
		
		private static readonly HttpClient httpClient = new HttpClient();
		
		public const string accountFile = "AWSCloudTasks_account.json";

		// SECURITY: WARNING: This is stored in plain text
		// Regions: https://docs.aws.amazon.com/general/latest/gr/cognito_identity.html
		[Serializable]
		private class Account {
			public string username;
			public string password;
			public string identity;
			public string userpool; // Cognito User Pool
			public string clientId; // Cognito User Pool Client App Id
			public string policyId; // Cognito Identity Pool Id
		}

		private Account account = null;

		private void LoadAccount() {
			// FIXME: persistentDataPath is project-specific, but this should be universal
			// NOTE: persistentDataPath is base_path/Company/Project/* so up two levels is universal
			var accountPath = Path.Combine(Application.persistentDataPath, accountFile);
			if (!File.Exists(accountPath)) {
				Debug.Log($"AWSCloudTasks missing account file {accountPath}");
				return;
			}
			var accountData = File.ReadAllText(accountPath);
			var accountJson = JsonConvert.DeserializeObject(accountData) as JObject;
			account = accountJson.ToObject<Account>();
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

		private void GetAuthentication() {
			// https://docs.aws.amazon.com/cognito-user-identity-pools/latest/APIReference/API_InitiateAuth.html
			var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
			var request = new HttpRequestMessage {
				Method = HttpMethod.Post,
				RequestUri = new Uri($"https://cognito-idp.{authorizationRegion}.amazonaws.com"),
				Headers = { { "X-Amz-Target", "AWSCognitoIdentityProviderService.InitiateAuth"} },
				Content = new StringContent(
					JsonConvert.SerializeObject(new {
						ClientId = account.clientId,
						AuthFlow = "USER_PASSWORD_AUTH",
						AuthParameters = new {
							USERNAME = account.username,
							PASSWORD = account.password
						}
					}),
					Encoding.UTF8,
					"application/x-amz-json-1.1" // Content-Type header
					)
			};
			//Debug.Log(request.ToString());
			var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
			//Debug.Log(response.ToString());
			var responseJson = response.Content.ReadAsStringAsync().Result;
			var responseObject = JsonConvert.DeserializeObject(responseJson) as JObject;
			
			var authenticationJson = responseObject["AuthenticationResult"] as JObject;
			authentication = authenticationJson.ToObject<Authentication>();
			if(authentication == null) {
				Debug.LogWarning($"WSCloudTasks unable to parse response {responseJson}");
			}
		}

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

		private void GetCredentials() {
			if(account.identity == null) {
				var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
				var loginsJson = new JObject();
				loginsJson[$"cognito-idp.{authorizationRegion}.amazonaws.com/{account.userpool}"] = authentication.IdToken;
			
				var contentJson = new JObject();
				contentJson["IdentityPoolId"] = account.policyId;
				contentJson["Logins"] = loginsJson;

				// https://docs.aws.amazon.com/cognitoidentity/latest/APIReference/API_GetId.html
				var credentialsRegion = account.policyId.Substring(0, account.policyId.IndexOf(':'));
				var request = new HttpRequestMessage {
					Method = HttpMethod.Post,
					RequestUri = new Uri($"https://cognito-identity.{credentialsRegion}.amazonaws.com"),
					Headers = {
						{ "X-Amz-Target", "AWSCognitoIdentityService.GetId" }
					},
					Content = new StringContent(
						contentJson.ToString(),
						Encoding.UTF8,
						"application/x-amz-json-1.1" // Content-Type header
					)
				};
				Debug.Log(request.ToString());
				var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
				Debug.Log(response.ToString());
				var responseJson = response.Content.ReadAsStringAsync().Result;
				var responseObject = JsonConvert.DeserializeObject(responseJson) as JObject;
				
				account.identity = responseObject["IdentityId"].ToString();
				// TODO: Identity will not change - this could be recorded
			}

			{
				var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
				var loginsJson = new JObject();
				loginsJson[$"cognito-idp.{authorizationRegion}.amazonaws.com/{account.userpool}"] = authentication.IdToken;
				
				var contentJson = new JObject();
				contentJson["IdentityId"] = account.identity;
				contentJson["Logins"] = loginsJson;
				
				// https://docs.aws.amazon.com/cognitoidentity/latest/APIReference/API_GetCredentialsForIdentity.html
				var credentialsRegion = account.policyId.Substring(0, account.policyId.IndexOf(':'));
				var request = new HttpRequestMessage {
					Method = HttpMethod.Post,
					RequestUri = new Uri($"https://cognito-identity.{credentialsRegion}.amazonaws.com"),
					Headers = {
						{ "X-Amz-Target", "AWSCognitoIdentityService.GetCredentialsForIdentity" }
					},
					Content = new StringContent(
						contentJson.ToString(),
						Encoding.UTF8,
						"application/x-amz-json-1.1" // Content-Type header
					)
				};
				request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.1");
				Debug.Log(request.ToString());
				var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
				Debug.Log(response.ToString());
				var responseJson = response.Content.ReadAsStringAsync().Result;
				var responseObject = JsonConvert.DeserializeObject(responseJson) as JObject;
				
				var credentialsJson = responseObject["Credentials"] as JObject;
				credentials = credentialsJson.ToObject<Credentials>();
			}
		}

		public bool connected { get; private set; } = false;

		public AWSCloudTasks() {
			// Authentication flow: https://docs.aws.amazon.com/cognito/latest/developerguide/authentication-flow.html
			LoadAccount();
			if(account == null) return;
			GetAuthentication();
			if(authentication == null) return;
			GetCredentials();
			if(credentials == null) return;
			Debug.Log("AWSCloudTasks SUCCESS");

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
