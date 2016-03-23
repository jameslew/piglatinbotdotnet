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
//using Microsoft.Rest;

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
            //ConnectorClient foo = new ConnectorClient(new Uri("https://intercomscratch.azure-api.net"), new ConnectorClientCredentials());
            ConnectorClient connector = BotConnector.CreateConnectorClient("scratch");

            //Extract the per - user, per - bot data store from the incoming message to see if (a) this is a new user, and(b) whether they've seen 
            //the most recent legal documents
            var userData = message.GetBotUserData<pigLatinBotUserData>("v1");

            if (userData == null)
                userData = new pigLatinBotUserData();

            Message replyMessage = message.CreateReplyMessage();
            replyMessage.Language = "en";

            if (message.Type != "Message" && !handleSystemMessages(message, userData, connector))
            {
                replyMessage.Text = translateToPigLatin("PigLatinBot was unable to process the system message.");
                var welcomeResponse = Request.CreateResponse(HttpStatusCode.InternalServerError, replyMessage);
                return welcomeResponse;
            }

            if (message.Type == "Message")
            {
                if (message.Text == "MessageTypesTest")
                {
                    messageTypesTest(message);
                }

                replyMessage.Text = translateToPigLatin(message.Text.Trim());
                var Response = Request.CreateResponse(HttpStatusCode.OK, replyMessage);
                return Response;
            }

            var ResponseFinal = Request.CreateResponse(HttpStatusCode.OK);
            return ResponseFinal;
        }

        private bool handleSystemMessages(Message message, pigLatinBotUserData userData, ConnectorClient connector)
        {
            Message replyMessage = message.CreateReplyMessage();
            message.Language = "en";

            switch (message.Type)
            {
                case "DeleteUserData":
                    replyMessage.Type = message.Type;
                    replyMessage.Text = translateToPigLatin("I have deleted your data oh masterful one");
                    userData = new pigLatinBotUserData();
                    Trace.TraceInformation("Clearing user's BotUserData");
                    break;

                case "EndOfConversation":
                    replyMessage.Text = translateToPigLatin("Catch you later alligator");
                    replyMessage.Type = message.Type;
                    break;

                //if they're new or haven't seen the updated legal documents, send them a message
                //use the incoming message to set up the outgoing message
                case "UserAddedToConversation":
                    if (message.Mentions.Count() > 0)
                    {
                        bool needToSendWelcomeText = true;

                        if (userData.isNewUser == true)
                        {
                            userData.isNewUser = false;
                            needToSendWelcomeText = true;
                        }

                        if (userData.lastReadLegalese < lastModifiedLegalese)
                        {
                            userData.lastReadLegalese = DateTime.UtcNow;
                            needToSendWelcomeText = true;
                        }

                        if (needToSendWelcomeText)
                        {
                            replyMessage.Text = string.Format(translateToPigLatin("Welcome to the chat") + " {0}, " + translateToPigLatin("I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", message.Mentions[0].Text);
                            replyMessage.Type = message.Type;
                            replyMessage.Participants.Clear();
                            replyMessage.TotalParticipants = 2;
                            replyMessage.To = message.Mentions[0].Mentioned;
                            replyMessage.ConversationId = null;
                            replyMessage.ChannelConversationId = null;
                        }
                    }
                    else
                    {
                        Trace.TraceError("No mentions when user was added to conversation");
                        replyMessage.Text = string.Format(translateToPigLatin("Bummer, BotConnector didn't tell me who joined"));
                        replyMessage.Type = "Message";
                    }
                    break;

                case "BotAddedToConversation":
                    replyMessage.Text = string.Format(translateToPigLatin("Hey there, I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", message.Mentions[0].Text);
                    replyMessage.Type = message.Type;
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

            }

            replyMessage.SetBotUserData(userData, "v1");
            var Response = connector.Messages.SendMessageAsync(replyMessage).Result;
            return true;
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

        private bool messageTypesTest(Message message)
        {

            Message dmUser = message.CreateReplyMessage();
            Message replyDirected = message.CreateReplyMessage();
            Message replyBroadcast = message.CreateReplyMessage();
            Message newBroadcast = new Message();
            Message newDirected = new Message();
            Message replyMessage = message.CreateReplyMessage();
            ConnectorClient foo = new ConnectorClient(new Uri("https://intercomscratch.azure-api.net"), new ConnectorClientCredentials());

            try
            {
                dmUser.Text = "Should go to DM channel";
                dmUser.Type = "Message";
                dmUser.ConversationId = null;
                dmUser.ChannelConversationId = null;
                dmUser.Participants.Clear();
                dmUser.To = message.Mentions[0].Mentioned;
                dmUser.TotalParticipants = 2;

                var dmResponse = foo.Messages.SendMessageAsync(dmUser).Result;
            }
            catch (HttpRequestException e)
            {
                Trace.TraceError("Failed to send DM, error: {0}", e.InnerException.Message);
            }

            try
            {
                replyBroadcast.Text = "Should go to broadcast channel without a mention";
                replyBroadcast.Type = "Message";
                replyBroadcast.ConversationId = null;
                replyBroadcast.ChannelConversationId = null;
                replyBroadcast.To = new ChannelAccount() { ChannelId = message.Mentions[0].Mentioned.ChannelId };
                var bcReply = foo.Messages.SendMessageAsync(replyBroadcast).Result;
            }
            catch (HttpRequestException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }


            try
            {
                replyDirected.Text = "Should go to broadcast channel with a mention";
                replyDirected.Type = "Message";
                replyDirected.ConversationId = null;
                replyDirected.ChannelConversationId = null;
                var rdReply = foo.Messages.SendMessageAsync(replyDirected).Result;
            }
            catch (HttpRequestException e)
            {
                Trace.TraceError("Failed to send broadcast with mention, error: {0}", e.InnerException.Message);
            }


            try
            {
                newBroadcast.Text = "Should go to broadcast channel without a mention";
                newBroadcast.Type = "Message";
                newBroadcast.To = new ChannelAccount() { ChannelId = message.Mentions[0].Mentioned.ChannelId };
                var nbReply = foo.Messages.SendMessageAsync(newBroadcast).Result;
            }
            catch (HttpRequestException e)
            {
                Trace.TraceError("Failed to send new broadcast without mention, error: {0}", e.InnerException.Message);
            }


            try
            {
                newDirected.Text = "Should go to group channel directed with a mention";
                newDirected.Type = "Message";
                newDirected.To = message.From;
                var nDReply = foo.Messages.SendMessageAsync(newDirected).Result;
            }
            catch (HttpRequestException e)
            {
                Trace.TraceError("Failed to send directed group message with mention, error: {0}", e.InnerException.Message);
            }
            return true;
        }
    }
}

