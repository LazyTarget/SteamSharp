﻿using Newtonsoft.Json;
using Org.BouncyCastle.Security;
using SteamSharp.Helpers;
using SteamSharp.Helpers.Cryptography;
using System;
using System.Net;
using System.Net.Http;
using System.Text;

namespace SteamSharp.Authenticators {

	/// <summary>
	/// Authenticatior allowing access to Steam resources protected by user credentials (username, password)
	/// </summary>
	public class UserAuthenticator : ISteamAuthenticator {

		internal string AccessToken { get; private set; }

		/// <summary>
		/// Method invoked by the library in order to authenticate for a resource.
		/// Should not be called directly by consumer code.
		/// </summary>
		/// <param name="client">SteamClient instance to authenticate.</param>
		/// <param name="request">Request requiring authentication.</param>
		public void Authenticate( SteamClient client, ISteamRequest request ) {
			if( AccessToken != null )
				request.AddParameter( "access_token", AccessToken, ParameterType.QueryString );
		}

		/// <summary>
		/// Invoke method to initialize the authenticator (which should then be added to the Authenticator property of a <see cref="SteamClient"/> instance).
		/// </summary>
		/// <param name="accessToken">Access Token generated by the <see cref="UserAuthenticator.GetAccessCookieForUser"/> method.</param>
		/// <returns><see cref="UserAuthenticator"/> object for authentication of a <see cref="SteamClient"/> instance.</returns>
		public static UserAuthenticator ForProtectedResource( string accessToken ) {
			return new UserAuthenticator {
				AccessToken = accessToken
			};
		}

		/// <summary>
		/// Invoke method to initialize the authenticator (which should then be added to the Authenticator property of a <see cref="SteamClient"/> instance).
		/// </summary>
		/// <param name="user">User to be authenticated. This user object must possess a non-null AccessToken property.</param>
		/// <returns><see cref="UserAuthenticator"/> object for authentication of a <see cref="SteamClient"/> instance.</returns>
		public static UserAuthenticator ForProtectedResource( SteamUser user ) {
			return new UserAuthenticator {
				AccessToken = user.OAuthAccessToken
			};
		}

		/// <summary>
		/// Queries Steam API with user credentials and returns a valid access token for use in API calls.
		/// </summary>
		/// <param name="username">Username of the user requesting authentication.</param>
		/// <param name="password">Password for the user requesting authentication.</param>
		/// <param name="steamGuardAnswer"></param>
		/// <param name="captchaAnswer"></param>
		/// <returns>Access token which can then be used with the UserAuthenticator.ForProtectedResource method.</returns>
		public static SteamAccessRequestResult GetAccessCookieForUser( string username, string password, SteamGuardAnswer steamGuardAnswer = null, CaptchaAnswer captchaAnswer = null ) {

			RSAValues publicKey = GetRSAKeyValues( username );

			// RSA Encryption
			RSAHelper rsa = new RSAHelper();
			rsa.ImportParameters( new RSAParameters {
				E = publicKey.PublicKeyExponent.HexToByteArray(),
				N = publicKey.PublicKeyModulus.HexToByteArray()
			} );

			byte[] cipherPassword = rsa.Encrypt( Encoding.UTF8.GetBytes( password ) );
			string encodedCipherPassword = Convert.ToBase64String( cipherPassword );
			
			SteamClient client = new SteamClient( "https://steamcommunity.com/" );
			SteamRequest request = new SteamRequest( "mobilelogin/dologin", HttpMethod.Post );
			
			request.AddParameter( "username", username, ParameterType.QueryString );
			request.AddParameter( "password", encodedCipherPassword, ParameterType.QueryString );
			request.AddParameter( "rsatimestamp", publicKey.Timestamp, ParameterType.QueryString );
			request.AddParameter( "oauth_client_id", "DE45CD61", ParameterType.QueryString );
			request.AddParameter( "oauth_scope", "read_profile write_profile read_client write_client", ParameterType.QueryString );

			if( captchaAnswer != null ) {
				request.AddParameter( "captchagid", captchaAnswer.GID, ParameterType.QueryString );
				request.AddParameter( "captcha_text", captchaAnswer.SolutionText, ParameterType.QueryString );
			}

			if( steamGuardAnswer != null ) {
				request.AddParameter( "emailsteamid", steamGuardAnswer.ID, ParameterType.QueryString );
				request.AddParameter( "emailauth", steamGuardAnswer.SolutionText, ParameterType.QueryString );
			}

			ISteamResponse response = client.Execute( request );
			if( !response.IsSuccessful )
				throw new SteamRequestException( "User authentication failed. Request to procure Steam access token failed (HTTP request not successful).", response ) {
					IsRequestIssue = true
				};

			SteamTokenResult result;

			try {
				result = JsonConvert.DeserializeObject<SteamTokenResult>( response.Content );
			} catch( Exception e ) {
				throw new SteamRequestException( "Unable to deserialize the token response from Steam.", e ) {
					IsDeserializationIssue = true
				};
			}

			if( !result.IsSuccessful ){
				return new SteamAccessRequestResult {
					IsSuccessful = false,
					SteamResponseMessage = result.Message,
					IsCaptchaNeeded = result.IsCaptchaNeeded,
					CaptchaURL = ( String.IsNullOrEmpty( result.CaptchaGID ) ) ? null : "https://steamcommunity.com/public/captcha.php?gid=" + result.CaptchaGID,
					CaptchaGID = ( String.IsNullOrEmpty( result.CaptchaGID ) ) ? null : result.CaptchaGID,
					IsSteamGuardNeeded = result.IsEmailAuthNeeded,
					SteamGuardID = ( String.IsNullOrEmpty( result.EmailSteamID ) ) ? null : result.EmailSteamID,
					SteamGuardEmailDomain = ( String.IsNullOrEmpty( result.EmailDomain ) ) ? null : result.EmailDomain
				};
			}

			if( result.OAuthParams == null )
				throw new SteamRequestException( "Login was successful did the response did not contain expected OAuth access information.", response );

			OAuthParameters oauthParams = JsonConvert.DeserializeObject<OAuthParameters>( result.OAuthParams );

			SteamUser user = new SteamUser {
				SteamID = new SteamID( oauthParams.SteamID ),
				OAuthAccessToken = oauthParams.OAuthToken
			};

			return new SteamAccessRequestResult {
				IsSuccessful = true,
				IsLoginComplete = result.IsLoginComplete,
				User = user
			};

		}

		private static RSAValues GetRSAKeyValues( string username ) {

			SteamClient client = new SteamClient( "https://steamcommunity.com/" );
			SteamRequest request = new SteamRequest( "login/getrsakey" );
			request.AddParameter( "username", username, ParameterType.QueryString );

			ISteamResponse response = client.Execute( request );

			if( !response.IsSuccessful )
				throw new SteamRequestException( "User authentication failed. Request to procure Steam RSA Key failed (HTTP request not successful).", response ) {
					IsRequestIssue = true
				};

			RSAValues result = JsonConvert.DeserializeObject<RSAValues>( response.Content );

			if( !result.Success || String.IsNullOrEmpty( result.PublicKeyModulus ) || String.IsNullOrEmpty( result.PublicKeyExponent ) )
				throw new SteamAuthenticationException( "Unable to authenticate user. Likely the username supplied is invalid." );

			return result;

		}

		private class RSAValues {

			/// <summary>
			/// Boolean value indicating whether or not the key generation was successful.
			/// </summary>
			public bool Success { get; set; }

			/// <summary>
			/// Modulus value to be used for RSA encryption.
			/// </summary>
			[JsonProperty( "publickey_mod" )]
			public string PublicKeyModulus { get; set; }

			/// <summary>
			/// Exponent value to be used for RSA encryption.
			/// </summary>
			[JsonProperty( "publickey_exp" )]
			public string PublicKeyExponent { get; set; }

			/// <summary>
			/// RSA Timestamp
			/// </summary>
			public string Timestamp { get; set; }

		}

		private class SteamTokenResult {

			/// <summary>
			/// Boolean value indicating whether or not the key request was successful.
			/// </summary>
			[JsonProperty( "success" )]
			public bool IsSuccessful { get; set; }

			[JsonProperty( "login_complete" )]
			public bool IsLoginComplete { get; set; }

			public string Message { get; set; }

			[JsonProperty( "captcha_needed" )]
			public bool IsCaptchaNeeded { get; set; }

			[JsonProperty( "captcha_gid" )]
			public string CaptchaGID { get; set; }

			[JsonProperty( "emailauth_needed" )]
			public bool IsEmailAuthNeeded { get; set; }

			public string EmailDomain { get; set; }

			public string EmailSteamID { get; set; }

			[JsonProperty( "oauth" )]
			public string OAuthParams { get; set; }

		}

		/// <summary>
		/// Result object, delivered in SteamTokenResult, which contains data about the access request.
		/// </summary>
		private class OAuthParameters {

			[JsonProperty( "steamid" )]
			public string SteamID { get; set; }

			[JsonProperty( "oauth_token" )]
			public string OAuthToken { get; set; }

			[JsonProperty( "webcookie" )]
			public string WebCookie { get; set; }

		}

		/// <summary>
		/// Result object for a Steam GetAccessToken request. It should be evaluated to determine if additional action is needed by the user (SteamGuard, Captcha).
		/// </summary>
		public class SteamAccessRequestResult {

			/// <summary>
			/// Response message from the Steam request.
			/// </summary>
			public string SteamResponseMessage { get; set; }

			/// <summary>
			/// Flag indicating whether or not a captcha must be solved before a token can be returned.
			/// </summary>
			public bool IsCaptchaNeeded { get; set; }

			/// <summary>
			/// If a captcha must be solved to continue, this will be the URL location of the captcha image.
			/// If no captcha must be solved, this will be null.
			/// </summary>
			public string CaptchaURL { get; set; }

			/// <summary>
			/// If a captcha must be solved to continue, this unique GID cooresponding to this CAPTCHA (needed when submitting the user's solution).
			/// If no captcha must be solved, this will be null.
			/// </summary>
			public string CaptchaGID { get; set; }

			/// <summary>
			/// Flag indiciating whether or not a SteamGuard code must be provided before a token can be returned.
			/// </summary>
			public bool IsSteamGuardNeeded { get; set; }

			/// <summary>
			/// Indicator of which SteamGuard mechanism was used to procure the code. Must be passed back on SteamGuard completion.
			/// </summary>
			public string SteamGuardID { get; set; }

			/// <summary>
			/// If available, gives the domain of the user's e-mail address where they can expect the SteamGuard code.
			/// </summary>
			public string SteamGuardEmailDomain { get; set; }

			/// <summary>
			/// Flag indicating whether or not the access token has been issued.
			/// </summary>
			public bool IsSuccessful { get; set; }

			/// <summary>
			/// Flag indicating if the login was successful and the transaction has been completed.
			/// </summary>
			public bool IsLoginComplete { get; set; }

			/// <summary>
			/// URL for Steam Transfer.
			/// </summary>
			public string TransferURL { get; set; }

			/// <summary>
			/// Object represent the user, complete with metadata and access token.
			/// </summary>
			public SteamUser User { get; set; }

		}

		/// <summary>
		/// Object for passing in the required CAPTCHA information on an authentication.
		/// </summary>
		public class CaptchaAnswer {

			/// <summary>
			/// GID originally provided for the CaptchaGID as part of the <see cref="SteamAccessRequestResult"/>.
			/// </summary>
			public string GID { get; set; }

			/// <summary>
			/// String representing the answer for the CAPTCHA. This should be provided by the user (unless you feel like spinning up some epic pattern recognition :)).
			/// </summary>
			public string SolutionText { get; set; }

		}

		/// <summary>
		/// Object for passing in the required SteamGuard information on an authentication.
		/// </summary>
		public class SteamGuardAnswer {

			/// <summary>
			/// GID originally provided for the SteamGuardID as part of the <see cref="SteamAccessRequestResult"/>.
			/// </summary>
			public string ID { get; set; }

			/// <summary>
			/// String representing the answer for the SteamGuard, as obtained by the user from their e-mail (or some other communication proof).
			/// </summary>
			public string SolutionText { get; set; }

		}

	}

}