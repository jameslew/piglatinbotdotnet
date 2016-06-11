﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Connector;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Rest;
using System.Diagnostics;
using System.Text;

namespace PigLatinBot
{
    public class pigLatinBotUserData
    {
        public bool isNewUser = true;
        public DateTime lastReadLegalese = DateTime.MinValue;
    }

   // [Microsoft.Bot.Connector.BotAuthentication]
    public class MessagesController : ApiController
    {
        private DateTime lastModifiedPolicies = DateTime.Parse("2015-10-01");

        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and reply to it, either directly or as an async delayed response
        /// </summary>
        /// <param name="message"></param>
        [ResponseType(typeof(Microsoft.Bot.Connector.Activity))]
        public HttpResponseMessage Post([FromBody]Activity message)
        {
            //ConnectorClient connector = new ConnectorClient(new Uri("https://intercomScratch.azure-api.net"), new Microsoft.Bot.Connector.MicrosoftAppCredentials());
            //ConnectorClient connector = new ConnectorClient(appId, appsecret);
            //ConnectorClient connector = new ConnectorClient();
            ConnectorClient connector = new ConnectorClient(new Uri(message.ServiceUrl));


            Activity replyMessage = message.CreateReply();
            replyMessage.Locale = "en";

            if (message.Type != "message")
            {
                replyMessage = handleSystemMessages(message, connector);
                if(replyMessage != null)
                {
                    var reply = connector.Conversations.ReplyToConversationAsync(message.Conversation.Id, replyMessage).Result;
                }
            }
            else
            {
                if (message.Text.Contains("MessageTypesTest"))
                {
                    messageTypesTest(message, connector);
                }

                replyMessage.Text = translateToPigLatin(message.Text.Trim());
                connector.Conversations.ReplyToConversation(replyMessage.Conversation.Id, replyMessage);
                var Response = Request.CreateResponse(HttpStatusCode.OK);
                return Response;
            }

            var responseOtherwise = Request.CreateResponse(HttpStatusCode.OK);
            return responseOtherwise;
        }

        private Activity handleSystemMessages(Activity message, ConnectorClient connector)
        {

            Activity replyMessage = message.CreateReply();
            message.Locale = "en";

            switch (message.Type)
            {
                case "DeleteUserData":
                    //In this case the DeleteUserData message comes from the user so we can clear the data and set it back directly
                    var userData = new pigLatinBotUserData();

                    StateClient pigLatinStateClient = new StateClient(new Uri("https://intercomScratch.azure-api.net"), new Microsoft.Bot.Connector.MicrosoftAppCredentials());
                    // The easy way, just reply with the message
                    //pigLatinStateClient.BotState.GetUserData("v1", userData);

                    //the hard way; not piggy backing on the reply message
                    //BotData deleteBotData = connector.Bots.GetUserData(message.To.Id, message.From.Id);
                    //pigLatinBotUserData deletedUserData = new pigLatinBotUserData();
                    //deleteBotData.SetProperty("v1", deletedUserData);
                    //connector.Bots.SetUserData(message.To.Id, message.From.Id, deleteBotData);
                    
                    replyMessage.Type = message.Type;
                    replyMessage.Text = translateToPigLatin("I have deleted your data oh masterful one");
                    Trace.TraceInformation("Clearing user's BotUserData");
                    return replyMessage;

                case "EndOfConversation":
                    replyMessage.Text = translateToPigLatin("Catch you later alligator");
                    replyMessage.Type = message.Type;
                    return replyMessage;

                //if they're new or haven't seen the updated legal documents, send them a message
                //use the incoming message to set up the outgoing message
                case "UserAddedToConversation":
                    if (message.Entities.Where(p=>p.Type == "Mention").Count() > 0)
                    {
                        bool needToSendWelcomeText = false;
                        //pigLatinBotUserData addedUserData = new pigLatinBotUserData();
                        //BotData botData = new BotData();

                        try
                        {
                        //    botData = connector.Bots.GetUserData(message.To.Id, message.Mentions[0].Mentioned.Id);
                        //    addedUserData = botData.GetProperty<pigLatinBotUserData>("v1");
                        }
                        catch { }
        
                        //if (addedUserData == null)
                        //    addedUserData = new pigLatinBotUserData();

                        //if (addedUserData.isNewUser == true)
                        //{
                        //    addedUserData.isNewUser = false;
                        //    needToSendWelcomeText = true;
                        //}

                        //if (addedUserData.lastReadLegalese < lastModifiedPolicies)
                        //{
                        //    addedUserData.lastReadLegalese = DateTime.UtcNow;
                        //    needToSendWelcomeText = true;
                        //}

                        if (needToSendWelcomeText)
                        {
                            var t3 = message.Entities.Where(p => p.Type == "Mention");
                            var mention3 = (Mention)t3.First();
                            replyMessage.Text = string.Format(translateToPigLatin("Welcome to the chat") + " {0}, " + translateToPigLatin("I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", mention3.Text);
                            replyMessage.Recipient = mention3.Mentioned;
                            replyMessage.Conversation = null;
 //                           replyMessage.ChannelConversationId = null;

                            //botData.SetProperty("v1", addedUserData);
                            //connector.Bots.SetUserData(message.To.Id, message.Mentions[0].Mentioned.Id, botData);
                        }
                        replyMessage.Type = message.Type;
                    }
                    else
                    {
                        Trace.TraceError("No mentions when user was added to conversation");
                        replyMessage.Text = string.Format(translateToPigLatin("Bummer, BotConnector didn't tell me who joined"));
                        replyMessage.Type = "Message";
                    }
                    return replyMessage;

                case "BotAddedToConversation":
                    var t = message.Entities.Where(p => p.Type == "Mention");
                    var mention = (Mention)t.First();
                    replyMessage.Text = string.Format(translateToPigLatin("Hey there, I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", mention.Text);
                    replyMessage.Type = message.Type;
                    return replyMessage;

                case "UserRemovedFromConversation":

                    if (message.Entities.Where(p => p.Type == "Mention").Count() > 0)
                    {
                        var t2 = message.Entities.Where(p => p.Type == "Mention");
                        var mention2 = (Mention)t2.First();
                        replyMessage.Recipient = mention2.Mentioned;

                        replyMessage.Text = string.Format("{0}", mention2.Text) + translateToPigLatin(" has Left the building");
                        replyMessage.Type = message.Type;
                    }
                    else
                    {
                        Trace.TraceError("No mentions when user was removed from conversation");
                        replyMessage.Text = string.Format(translateToPigLatin("Bummer, BotConnector didn't tell me who left"));
                        replyMessage.Type = "Message";
                    }
                    return replyMessage;

                default:
                    return null;

            }
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

        private Activity messageTypesTest(Activity message, ConnectorClient foo)
        {

            StringBuilder sb = new StringBuilder();
            // DM a user 
            try
            {
                Activity newDirectToUser = new Activity()
                {
                    Text = "Should go directly to user",
                    From = message.Recipient,
                    Recipient = message.From            
                };
                var dmResponse = foo.Conversations.StartConversationAsync(newDirectToUser).Result;
                sb.AppendLine(dmResponse.Message);
                sb.AppendLine();
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send DM, error: {0}", e.InnerException.Message);
            }

            // start new conversation with group of people
            //try
            //{
            //    string channelId = message.From.ChannelId;
            //    Message newDirectToUser = new Message()
            //    {
            //        Text = "Should go to directly to list of people",
            //        From = message.To,
            //        To = new ChannelAccount() { ChannelId = channelId },
            //        Participants = new ChannelAccount[] {
            //            new ChannelAccount() { ChannelId = channelId, Address = "user1@contoso.com" },
            //            new ChannelAccount() { ChannelId = channelId, Address = "user2@contoso.com" },
            //        }
            //    };
            //    var dmResponse = foo.Messages.SendMessageAsync(newDirectToUser).Result;
            //    sb.AppendLine(dmResponse.Text);
            //    sb.AppendLine();
            //}
            //catch (HttpOperationException e)
            //{
            //    Trace.TraceError("Failed to send DM, error: {0}", e.InnerException.Message);
            //}
            
            // message to conversation not directed to user using CreateReply
            try
            {
                Activity replyToConversation = message.CreateReply("Should go to conversation, but does not address the user that generated it");
                var bcReply = foo.Conversations.ReplyToConversationAsync(message.Conversation.Id, replyToConversation).Result;
                sb.AppendLine(bcReply.Message);
                sb.AppendLine();
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to user using CreateReply
            try
            {
                Activity replyToConversation = message.CreateReply("Should go to conversation, but addressing the user that generated it");
                var reply = foo.Conversations.ReplyToConversationAsync(message.Conversation.Id, replyToConversation).Result;
                sb.AppendLine(reply.Message);
                sb.AppendLine();
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            return message.CreateReply(sb.ToString());
        }
    }
}

