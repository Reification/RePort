using System;
using System.IO;
using System.Net.Http;
using System.Text;

// Log errors in Unity console
// TODO: Instead, throw with error log as message
using UnityEngine; // Used for JsonUtility (could use C# serialization instead)

namespace Reification.CloudTasks.AWS {
	public class Cognito {
		// QUESTION: Two endpoints are used - should two clients be used?
		// https://medium.com/@nuno.caneco/c-httpclient-should-not-be-disposed-or-should-it-45d2a8f568bc
		private static readonly HttpClient httpClient = new HttpClient();

		public const string accountFile = "AWSCloudTasks_account.json";
		
		// TODO: Option for user to exclude password from account file
		// TODO: Option to attempt authentication WITHOUT user input (so fail if password or MFA is needed)
		
		public bool authenticated {
			get {
				// TODO: Use RefreshToken instead of forcing reinitialization
				if(credentials != null && credentials.Expiration < new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()) {
					account = null;
					authentication = null;
					credentials = null;
				}
				
				// Authentication flow: https://docs.aws.amazon.com/cognito/latest/developerguide/authentication-flow.html
				// NOTE: Identity only needs to be retrieved once
				if(account == null && ! LoadAccount()) return false;
				if(authentication == null && !GetAuthentication()) return false;
				if(string.IsNullOrEmpty(account.identity) && !GetIdentity()) return false;
				if(credentials == null && !GetCredentials()) return false;
				return true;
			}
		}

		private void Authenticate() {
		}

		// TODO: Class methods to extract regions
		// Regions: https://docs.aws.amazon.com/general/latest/gr/cognito_identity.html

		// SECURITY: WARNING: Account is stored in plain text
		[Serializable]
		private class Account {
			public string username;
			public string password;
			public string identity;
			public string userpool; // Cognito User Pool
			public string clientId; // Cognito User Pool Client App Id
			public string policyId; // Cognito Identity Pool Id
			public string cloudUrl; // S3 Bucket Subdomain
		}

		private Account account = null;

		public string identity {
			get => account?.identity;
		}

		public string cloudUrl {
			get => account?.cloudUrl;
		}

		private bool LoadAccount() {
			// FIXME: persistentDataPath is project-specific, but this should be universal
			// NOTE: persistentDataPath is base_path/Company/Project/* so up two levels is universal
			var accountPath = Path.Combine(Application.persistentDataPath, accountFile);
			if(!File.Exists(accountPath)) {
				Debug.Log($"Missing account file {accountPath} -> AWS CloudTasks disabled");
				return false;
			}

			var accountData = File.ReadAllText(accountPath);
			account = JsonUtility.FromJson<Account>(accountData);
			if(account is null) {
				Debug.LogWarning($"AWSCloudTasks unable to read account file {accountPath}");
			}

			return true;
		}

		// QUESTION: Can a user have multiple concurrent sessions?
		// PROBLEM: If not, multiple machines sharing an account may repeatedly preempt each other.

		// User Pool authentication result on success
		// NOTE: member names match keys in AWS JSON response
		[Serializable]
		private class AuthenticationClass {
			public string AccessToken;
			public string IdToken;
			public string RefreshToken;
			public string TokenType;
			public string ExpiresIn;
		}

		private AuthenticationClass authentication = null;

		[Serializable]
		private class AuthenticationRequest {
			public string ClientId;
			public string AuthFlow;

			// NOTE: Request can include unused null parameters
			[Serializable]
			public class AuthParametersClass {
				// USER_PASSWORD_AUTH
				public string USERNAME;
				public string PASSWORD;

				// REFRESH_TOKEN_AUTH
				public string REFRESH_TOKEN;
			};

			public AuthParametersClass AuthParameters = null;
		}

		// NOTE: Response will default-construct missing members
		[Serializable]
		private class AuthenticationResponse {
			// AUTHENTICATED
			public AuthenticationClass AuthenticationResult = null;

			// CHALLENGE
			public string ChallengeName = null;

			[Serializable]
			public class ChallengeParametersClass {
				// QUESTION: What parameter members go here?
			};

			public ChallengeParametersClass ChallengeParameters = null;
			public string Session;
		}

		private bool GetAuthentication() {
			// https://docs.aws.amazon.com/cognito-user-identity-pools/latest/APIReference/API_InitiateAuth.html
			var authRequest = new AuthenticationRequest {
				ClientId = account.clientId,
				AuthFlow = "USER_PASSWORD_AUTH",
				AuthParameters = new AuthenticationRequest.AuthParametersClass {
					USERNAME = account.username,
					PASSWORD = account.password
				}
			};
			var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
			var request = new HttpRequestMessage {
				Method = HttpMethod.Post,
				RequestUri = new Uri($"https://cognito-idp.{authorizationRegion}.amazonaws.com"),
				Headers = { { "X-Amz-Target", "AWSCognitoIdentityProviderService.InitiateAuth" } },
				Content = new StringContent(
					JsonUtility.ToJson(authRequest),
					Encoding.UTF8,
					"application/x-amz-json-1.1" // Content-Type header
				)
			};
			var response = httpClient.SendAsync(request).Result;
			if(!response.IsSuccessStatusCode) {
				Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
				return false;
			}

			var responseString = response.Content.ReadAsStringAsync().Result;
			var authenticationResponse = JsonUtility.FromJson<AuthenticationResponse>(responseString);

			if(authenticationResponse.ChallengeName != null) {
				Debug.LogWarning($"Authentication unhandled challenge response: {authenticationResponse.ChallengeName}");
				// TODO: Handle challenge requests (maintain state so that user input can occur)
				// Is there a challenge response to request a token refresh? Just check for timeout?
				// NEW_PASSWORD_REQUIRED
				// MFA_SETUP (Maybe also works for TOTP_MFA)
				// TOTP_MFA / SOFTWARE_TOKEN_MFA
				// https://docs.aws.amazon.com/cognito/latest/developerguide/user-pool-settings-mfa-totp.html
				return false;
			}

			// NOTE: AuthenticationResult will be non-null, even if key is missing
			authentication = authenticationResponse.AuthenticationResult;
			if(authentication?.IdToken == null) {
				Debug.LogWarning($"Authentication cannot deserialize response:\n{responseString}");
				authentication = null;
				return false;
			}

			return true;
		}

		[Serializable]
		private class IdentityResponse {
			public string IdentityId;
		}

		private bool GetIdentity() {
			// https://docs.aws.amazon.com/cognitoidentity/latest/APIReference/API_GetId.html
			// NOTE: Identity request JSON has a generated key, so member serialization will not work
			var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
			var contentString = "{"
				+ $"\"IdentityPoolId\":\"{account.policyId}\","
				+ "\"Logins\":{"
				+ $"\"cognito-idp.{authorizationRegion}.amazonaws.com/{account.userpool}\":\"{authentication.IdToken}\""
				+ "}"
				+ "}";
			var credentialsRegion = account.policyId.Substring(0, account.policyId.IndexOf(':'));
			var request = new HttpRequestMessage {
				Method = HttpMethod.Post,
				RequestUri = new Uri($"https://cognito-identity.{credentialsRegion}.amazonaws.com"),
				Headers = {
					{ "X-Amz-Target", "AWSCognitoIdentityService.GetId" }
				},
				Content = new StringContent(
					contentString,
					Encoding.UTF8,
					"application/x-amz-json-1.1" // Content-Type header
				)
			};
			var response = httpClient.SendAsync(request).Result;
			if(!response.IsSuccessStatusCode) {
				Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
				return false;
			}

			var responseString = response.Content.ReadAsStringAsync().Result;
			var identityResponse = JsonUtility.FromJson<IdentityResponse>(responseString);

			account.identity = identityResponse.IdentityId;
			if(string.IsNullOrEmpty(account.identity)) {
				Debug.LogWarning($"GetIdentity cannot deserialize response:\n{responseString}");
				account.identity = null;
				return false;
			}
			
			// TODO: Identity will not change - account update could be recorded
			return true;
		}

		// Identity Pool credentials result on success
		// NOTE: member names match keys in AWS JSON response
		[Serializable]
		public class Credentials {
			public string AccessKeyId;
			public string SecretKey;
			public string SessionToken;
			public long Expiration;
		}

		public Credentials credentials { get; private set; }

		[Serializable]
		private class CredentialsResponse {
			public Credentials Credentials;
			public string IdentityId;
		}

		private bool GetCredentials() {
			// https://docs.aws.amazon.com/cognitoidentity/latest/APIReference/API_GetCredentialsForIdentity.html
			// NOTE: Identity request JSON has a generated key, so member serialization will not work
			var authorizationRegion = account.userpool.Substring(0, account.userpool.IndexOf('_'));
			var contentString = "{"
				+ $"\"IdentityId\":\"{account.identity}\","
				+ "\"Logins\":{"
				+ $"\"cognito-idp.{authorizationRegion}.amazonaws.com/{account.userpool}\":\"{authentication.IdToken}\""
				+ "}"
				+ "}";
			var credentialsRegion = account.policyId.Substring(0, account.policyId.IndexOf(':'));
			var request = new HttpRequestMessage {
				Method = HttpMethod.Post,
				RequestUri = new Uri($"https://cognito-identity.{credentialsRegion}.amazonaws.com"),
				Headers = {
					{ "X-Amz-Target", "AWSCognitoIdentityService.GetCredentialsForIdentity" }
				},
				Content = new StringContent(
					contentString,
					Encoding.UTF8,
					"application/x-amz-json-1.1" // Content-Type header
				)
			};
			var response = httpClient.SendAsync(request).Result as HttpResponseMessage;
			if(!response.IsSuccessStatusCode) {
				Debug.LogWarning($"Request failed with status code = {response.StatusCode}");
				return false;
			}

			var responseString = response.Content.ReadAsStringAsync().Result;
			var credentialsResponse = JsonUtility.FromJson<CredentialsResponse>(responseString);
			credentials = credentialsResponse.Credentials;

			if(credentials?.SecretKey == null) {
				Debug.LogWarning($"GetCredentials cannot deserialize response:\n{responseString}");
				credentials = null;
				return false;
			}

			Debug.Log("AWS CloudTasks enabled");
			return true;
		}
	}
}
