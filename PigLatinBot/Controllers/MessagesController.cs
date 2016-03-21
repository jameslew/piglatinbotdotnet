using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Connector.MicrosoftInternal;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Diagnostics;
using Microsoft.Rest;

namespace PigLatinBot
{
    public class pigLatinBotUserData
    {
        public bool isNewUser = true;
        public DateTime lastReadLegalese = DateTime.MinValue;
    }

    [Microsoft.Bot.Connector.Utilities.BotAuthentication]
    public class MessagesController : ApiController
    {
        private const string SESSION_COOKIE = "counter";
        private DateTime lastModifiedLegalese = DateTime.Parse("2015-10-01");
        private List<string> mentionChannels;

        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and reply to it, either directly or as an async delayed response
        /// </summary>
        /// <param name="message"></param>
        [ResponseType(typeof(Message))]
        public HttpResponseMessage Post([FromBody]Message message)
        {
            mentionChannels = new List<String>(new string[] { "slack", "groupme", "email", "telegram" });

            //Extract the per-user, per-bot data store from the incoming message to see if (a) this is a new user, and (b) whether they've seen 
            //the most recent legal documents
            var userData = message.GetBotUserData<pigLatinBotUserData>("v1");

            bool needToSendWelcomeText = false;
            //if they're new or haven't seen the updated legal documents, send them a message
            //use the incoming message to set up the outgoing message
            Message welcomeText = new Message()
            {
                Language = "en",
                To = message.From,
                From = message.To
            };

            if (userData == null)
                userData = new pigLatinBotUserData();

            if (userData.isNewUser == true)
            {
                welcomeText.Text = "Eyhay Ewbnay.";
                userData.isNewUser = false;
                needToSendWelcomeText = true;
            }

            if (userData.lastReadLegalese < lastModifiedLegalese)
            {
                welcomeText.Text += " Egallay ocumentsday areway <http://fuse.microsoft.com|erehay>.";
                userData.lastReadLegalese = DateTime.UtcNow;
                needToSendWelcomeText = true;
            }

            try
            {
                //Create a new Bot ConnectorClient to send the message object with
                Trace.TraceInformation("Decided this was a new user or needed legalese");
                if (needToSendWelcomeText)
                {
                    ConnectorClient foo = new ConnectorClient(new Uri("https://intercomppe.azure-api.net"), new ConnectorClientCredentials());
                    welcomeText.SetBotUserData(userData, "v1");
                    //postponing this for now
                    //var welcomeResponse = foo.Messages.SendMessageAsync(welcomeText).Result;
                }
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Exception sending OOB Legal Message");
                String error = JsonConvert.SerializeObject(e.Body);
                return Request.CreateResponse(HttpStatusCode.OK, error);
            }

            Message replyMessage = message.CreateReplyMessage();
            switch (message.Type)
            {
                case "DeleteUserData":
                    replyMessage.Type = message.Type;
                    replyMessage.Text = translateToPigLatin("I have deleted your data oh masterful one");
                    userData = new pigLatinBotUserData();
                    Trace.TraceInformation("Clearing user's BotUserData");
                    replyMessage.SetBotUserData(userData, "v1");
                    break;

                case "EndOfConversation":
                    replyMessage.Text = translateToPigLatin("Catch you later alligator");
                    replyMessage.Type = message.Type;
                    break;

                case "UserAddedToConversation":
                    if (message.Mentions.Count() > 0)
                    {
                        replyMessage.Text = string.Format(translateToPigLatin("Welcome to the chat") + " {0}, " + translateToPigLatin("I'm ReminderBot. I can help you remember to get things done.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.reminderbot.com)", message.Mentions[0].Text);
                        replyMessage.Type = message.Type;
                        replyMessage.ConversationId = null;
                        replyMessage.ChannelConversationId = null;
                        replyMessage.Participants.Clear();
                        replyMessage.TotalParticipants = 2;
                        ConnectorClient foo = new ConnectorClient(new Uri("https://intercomscratch.azure-api.net"), new ConnectorClientCredentials());
                        //ConnectorClient foo = BotConnector.CreateConnectorClient("ppe");
                        var welcomeResponse = foo.Messages.SendMessageAsync(replyMessage).Result;
                    }
                    else
                    {
                        Trace.TraceError("No mentions when user was added to conversation");
                        replyMessage.Text = string.Format(translateToPigLatin("Bummer, BotConnector didn't tell me who joined"));
                        replyMessage.Type = "Message";
                    }
                    break;

                case "UserRemovedFromConversation":
                    if (message.Mentions.Count() > 0)
                    {
                        replyMessage.Text = string.Format("{0}", message.Mentions[0].Text) + translateToPigLatin(" has Left the building");
                        replyMessage.Type = message.Type;
                    }
                    else
                    {
                        Trace.TraceError("No mentions when user was removed from conversation");
                        replyMessage.Text = string.Format(translateToPigLatin("Bummer, BotConnector didn't tell me who left"));
                        replyMessage.Type = "Message";
                    }
                    break;

                case "BotAddedToConversation":
                    replyMessage.Text = string.Format(translateToPigLatin("Hello welcome to the show starring ME"));
                    replyMessage.Type = message.Type;
                    break;

                case "Message":
                    replyMessage.Text = translateToPigLatin(message.Text.Trim());
                    if (mentionChannels.IndexOf(message.From.ChannelId) >= 0)
                    {
                        replyMessage.Text = "@" + message.From.Name + " " + replyMessage.Text;
                        replyMessage.Mentions = new List<Mention>();
                        replyMessage.Mentions.Add(new Mention(message.From, message.From.Name));
                    }
                    break;

            }

            replyMessage.Language = "en";
            var Response = Request.CreateResponse(HttpStatusCode.OK, replyMessage);
            return Response;
        }

        private string translateToPigLatin(string message)
        {
            string english = TrimPunctuation(message);
            string pigLatin = "";
            string firstLetter;
            string restOfWord;
            string vowels = "AEIOUaeiou";
            int letterPos;

            string outBuffer = "";
            foreach (string word in english.Split())
            {
                if (word == "") continue;
                firstLetter = word.Substring(0, 1);
                restOfWord = word.Substring(1, word.Length - 1);
                letterPos = vowels.IndexOf(firstLetter);
                if (letterPos == -1)
                {
                    //it's a consonant
                    pigLatin = restOfWord + firstLetter + "ay";
                }
                else
                {
                    //it's a vowel
                    pigLatin = word + "way";
                }
                outBuffer += pigLatin + " ";
            }
            return outBuffer.Trim();
        }

        /// &llt;summary>
        /// TrimPunctuation from start and end of string.
        /// </summary>
        static string TrimPunctuation(string value)
        {
            // Count start punctuation.
            int removeFromStart = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsPunctuation(value[i]) || value[i] == '@')
                {
                    removeFromStart++;
                }
                else
                {
                    break;
                }
            }

            // Count end punctuation.
            int removeFromEnd = 0;
            for (int i = value.Length - 1; i >= 0; i--)
            {
                if (char.IsPunctuation(value[i]))
                {
                    removeFromEnd++;
                }
                else
                {
                    break;
                }
            }
            // No characters were punctuation.
            if (removeFromStart == 0 &&
                removeFromEnd == 0)
            {
                return value;
            }
            // All characters were punctuation.
            if (removeFromStart == value.Length &&
                removeFromEnd == value.Length)
            {
                return "";
            }
            // Substring.
            return value.Substring(removeFromStart,
                value.Length - removeFromEnd - removeFromStart);
        }


        /// <summary>
        /// Example retrieving a session cookie and pulling a value from it
        /// </summary>
        /// <returns></returns>
        private TypeT GetSessionCookie<TypeT>(string name)
        {
            TypeT value = default(TypeT);
            CookieHeaderValue cookie = Request.Headers.GetCookies(name).FirstOrDefault();
            if (cookie != null)
            {
                value = JsonConvert.DeserializeObject<TypeT>(cookie[name].Value);
            }
            return value;
        }

        /// <summary>
        /// Example of tracking session state by serializing into a session cookie
        /// </summary>
        /// <param name="value"></param>
        /// <param name="response"></param>
        /// <returns></returns>
        private CookieHeaderValue SetSessionCookie<TypeT>(HttpResponseMessage response, string name, TypeT value, TimeSpan expiry)
        {
            CookieHeaderValue cookie = CreateCookieHeaderValue<TypeT>(name, value, expiry);
            response.Headers.AddCookies(new CookieHeaderValue[] { cookie });
            return cookie;
        }

        /// <summary>
        /// Example of routine to create a cookieHeaderValue used for setting cookie values as part of async callbacks
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private CookieHeaderValue CreateCookieHeaderValue<TypeT>(string name, TypeT value, TimeSpan expiry)
        {
            var cookie = new CookieHeaderValue(name, JsonConvert.SerializeObject(value));
            cookie.Expires = DateTimeOffset.Now + expiry;
            cookie.Domain = Request.RequestUri.Host;
            cookie.Path = "/";
            return cookie;
        }
    }
}

//    public class MessagesController : ApiController
//{
//    /// <summary>
//    /// POST: api/Messages
//    /// receive a message from a user and reply to it, either directly or as an async delayed response
//    /// </summary>
//    /// <param name="message"></param>
//    [ResponseType(typeof(Message))]
//    public HttpResponseMessage Post([FromBody]Message message)
//    {
//        Message replyMessage = message.CreateReplyMessage();
//        replyMessage.Text = "Wrong MessageType or MentionCount";
//        if (message.Type == "UserAddedToConversation")
//        {
//            //doesn't work; message goes to group channel instead of DM
//            replyMessage.Text = "Welcome";
//            replyMessage.Type = message.Type;
//            replyMessage.ConversationId = null;
//            replyMessage.ChannelConversationId = null;
//            replyMessage.Participants.Clear();
//            replyMessage.TotalParticipants = 2;

//            //doesn't work; crashes on SendMessageAsyc
//            try
//            {
//                ConnectorClient foo = new ConnectorClient(new Uri("https://intercomppe.azure-api.net"), new ConnectorClientCredentials());
//                var welcomeResponse = foo.Messages.SendMessageAsync(replyMessage).Result;
//            }
//            catch (HttpRequestException e)
//            {
//                Trace.TraceError("Crashed trying to SendMessageAsync {0}", e.InnerException.Message);
//            }

//            //doesn't work, crashes on CreateConnectorClient("ppe")
//            try
//            {
//                ConnectorClient foo = BotConnector.CreateConnectorClient("ppe");
//                var welcomeResponse = foo.Messages.SendMessageAsync(replyMessage).Result;
//            }
//            catch (HttpRequestException e)
//            {
//                Trace.TraceError("Crashed trying to CreateConnectorClient('ppe') {0}", e.InnerException.Message);
//            }


//        }
//        replyMessage.Language = "en";
//        var Response = Request.CreateResponse(HttpStatusCode.OK, replyMessage);
//        return Response;

//    }
