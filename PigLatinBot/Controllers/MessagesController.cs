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
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Web.Http.Controllers;

namespace PigLatinBot
{
    public class pigLatinBotUserData
    {
        public bool isNewUser = true;
        public DateTime lastReadLegalese = DateTime.MinValue;
    }

    //[BotAuthentication]
    [BotAuthentication(OpenIdConfigurationUrl = "https://aps-dev-0-skype.cloudapp.net/v1/.well-known/openidconfiguration")]
    public class MessagesController : ApiController
    {
        private DateTime lastModifiedPolicies = DateTime.Parse("2015-10-01");

        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and reply to it, either directly or as an async delayed response
        /// </summary>
        /// <param name="message"></param>
        [ResponseType(typeof(Activity))]
        public virtual async Task<HttpResponseMessage> Post([FromBody] object obj)
        {
            Activity[] activities = (obj is JObject) ? new Activity[] { ((JObject)obj).ToObject<Activity>() } : ((JArray)obj).ToObject<Activity[]>();

            foreach (var message in activities)
            {
                //ConnectorClient connector = new ConnectorClient(new Uri("https://intercomScratch.azure-api.net"), new Microsoft.Bot.Connector.MicrosoftAppCredentials());
                //ConnectorClient connector = new ConnectorClient(appId, appsecret);
                //ConnectorClient connector = new ConnectorClient();
                ConnectorClient connector = new ConnectorClient(new Uri(message.ServiceUrl));


                Activity replyMessage = message.CreateReply();
                replyMessage.Locale = "en-Us";
                replyMessage.Type = message.Type;

                if (message.GetActivityType() != ActivityTypes.Message)
                {
                    replyMessage = await handleSystemMessagesAsync(message, connector);
                    if (replyMessage != null)
                    {
                        var reply = await connector.Conversations.ReplyToActivityAsync(replyMessage);
                    }
                }
                else
                {
                    if (message.Text.Contains("MessageTypesTest"))
                    {
                        Activity mtResult = await messageTypesTest(message, connector);
                        mtResult.Type = message.Type;
                        await connector.Conversations.ReplyToActivityAsync(mtResult);
                    }
                    else if (message.Text.Contains("DataTypesTest"))
                    {
                        Activity dtResult = await dataTypesTest(message, connector);
                        dtResult.Type = message.Type;
                        await connector.Conversations.ReplyToActivityAsync(dtResult);
                    }
                   
                    try
                    {
                        replyMessage.Text = translateToPigLatin(message.Text);
                        var httpResponse = await connector.Conversations.ReplyToActivityAsync(replyMessage);
                    }
                    catch (HttpResponseException e)
                    {
                        Trace.WriteLine(e.Message);
                        var Response = Request.CreateResponse(HttpStatusCode.InternalServerError);
                        return Response;
                    }
                }
            }
            var responseOtherwise = Request.CreateResponse(HttpStatusCode.OK);
            return responseOtherwise;
        }
         

        private async Task<Activity> handleSystemMessagesAsync(Activity message, ConnectorClient connector)
        {

            StateClient pigLatinStateClient = new StateClient(new Uri(message.ChannelId=="emulator"?message.ServiceUrl: "https://intercom-api-scratch.azurewebsites.net"), new MicrosoftAppCredentials());
            BotState botState = new BotState(pigLatinStateClient);

            Activity replyMessage = message.CreateReply();
            replyMessage.Type = "message/text";
            message.Locale = "en";

            switch (message.GetActivityType())
            {
                case ActivityTypes.DeleteUserData:
                    //In this case the DeleteUserData message comes from the user so we can clear the data and set it back directly
                    BotData currentBotData = (BotData) await botState.GetUserDataAsync(message.ChannelId, message.From.Id);
                    pigLatinBotUserData deleteUserData = new pigLatinBotUserData();
                    currentBotData.SetProperty("v1", deleteUserData);
                    await botState.SetUserDataAsync(message.ChannelId, message.From.Id, currentBotData);
                    
                    replyMessage.Text = translateToPigLatin("I have deleted your data oh masterful one");
                    Trace.TraceInformation("Clearing user's BotUserData");
                    return replyMessage;

                //if they're new or haven't seen the updated legal documents, send them a message
                //use the incoming message to set up the outgoing message
                case ActivityTypes.ConversationUpdate:

                    foreach(ChannelAccount added in message.MembersAdded)
                    {
                        Activity addedMessage = message.CreateReply();

                        bool needToSendWelcomeText = false;
                        pigLatinBotUserData addedUserData = new pigLatinBotUserData();
                        BotData botData = new BotData();

                        // is the added member me?
                        if (added.Id == message.Recipient.Id)
                        {
                            addedMessage.Text = string.Format(translateToPigLatin("Hey there, I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", added.Name);
                            var reply = await connector.Conversations.ReplyToActivityAsync(addedMessage);
                            continue;
                        }
                        
                        // okay, check for real users
                        try
                        {
                            botData = (BotData) await botState.GetUserDataAsync(message.ChannelId, added.Id);
                        }
                        catch (Exception e)
                        {
                            if (e.Message == "Resource not found")
                            { }
                            else
                                throw e;
                        }
    
                        if(botData == null)
                            botData = new BotData(eTag: "*");

                        addedUserData = botData.GetProperty<pigLatinBotUserData>("v1") ?? new pigLatinBotUserData();

                        if (addedUserData.isNewUser == true)
                        {
                            addedUserData.isNewUser = false;
                            needToSendWelcomeText = true;
                        }

                        if (addedUserData.lastReadLegalese < lastModifiedPolicies)
                        {
                            addedUserData.lastReadLegalese = DateTime.UtcNow;
                            needToSendWelcomeText = true;
                        }
                        if (needToSendWelcomeText)
                        {
                            addedMessage.Text = string.Format(translateToPigLatin("Welcome to the chat") + " {0}, " + translateToPigLatin("I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", added.Name);
                            addedMessage.Recipient = added;
                            addedMessage.Conversation = null;
 
                            try
                            {
                                botData.SetProperty("v1", addedUserData);
                                var response = await botState.SetUserDataAsync(message.ChannelId, added.Id, botData);
                            }
                            catch (Exception e)
                            {
                                Trace.WriteLine(e.Message);
                            }
                            
                            var ConversationId = await connector.Conversations.CreateDirectConversationAsync(message.Recipient, message.From);
                            addedMessage.Conversation = new ConversationAccount(id: ConversationId.Id);
                            var reply = await connector.Conversations.SendToConversationAsync(addedMessage);
                        }
                    }

                    //maybe someone got removed
                    foreach (ChannelAccount removed in message.MembersRemoved)
                    {
                        Activity removedMessage = message.CreateReply();
                        removedMessage.Locale = "en";

                        removedMessage.Text = string.Format("{0}", removed.Name) + translateToPigLatin(" has Left the building");
                        var reply = await connector.Conversations.ReplyToActivityAsync(removedMessage);
                    }

                    return null;

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

        private async Task<Activity> messageTypesTest(Activity message, ConnectorClient connector)
        {

            StringBuilder sb = new StringBuilder();
            // DM a user 
            try
            {
                Activity newDirectToUser = new Activity()
                {
                    Text = "Should go directly to user",
                    Type = message.Type,
                    From = message.Recipient,
                    Recipient = message.From,
                    ChannelId = message.ChannelId
                };

                var ConversationId = await connector.Conversations.CreateDirectConversationAsync(message.Recipient, message.From);
                newDirectToUser.Conversation = new ConversationAccount(id: ConversationId.Id);
                var reply = await connector.Conversations.SendToConversationAsync(newDirectToUser);
                if (reply != null)
                    sb.AppendLine(reply.Message);
            }
            catch (Exception e)
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
                replyToConversation.Type = message.Type;
                //var bcReply = await connector.Conversations.ReplyToActivityAsync(replyToConversation);
                var bcReply = await connector.Conversations.SendToConversationAsync(replyToConversation);
                if(bcReply != null)
                    sb.AppendLine(bcReply.Message);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to user using CreateReply
            try
            {
                Activity replyToConversation = message.CreateReply("Should go to conversation, but addressing the user that generated it");
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = message.Type;
                //var reply = await connector.Conversations.ReplyToActivityAsync(replyToConversation);
                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
                if (reply != null)
                    sb.AppendLine(reply.Message);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to everyone with a PigLatin Card
            try
            {
                Activity replyToConversation = message.CreateReply(translateToPigLatin("Should go to conversation, with a fancy schmancy card"));
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = message.ChannelId == "skype" ? "message/card" : "message";
                replyToConversation.Attachments = new List<Attachment>();
                
                List<CardImage> cardImages = new List<CardImage>();
                cardImages.Add(new CardImage(url:"https://3.bp.blogspot.com/-7zDiZVD5kAk/T47LSvDM_jI/AAAAAAAAByM/AUhkdynaJ1Y/s200/i-speak-pig-latin.png"));

                List<CardAction> cardButtons = new List<CardAction>();

                CardAction plButton = new CardAction()
                {
                    Value = "https://en.wikipedia.org/wiki/Pig_Latin",
                    Type = "openUrl",
                    Title = "WikiPedia Page"
                };
                cardButtons.Add(plButton);

                HeroCard plCard = new HeroCard()
                {
                    Title = translateToPigLatin("I'm a hero card, aren't I fancy?"),
                    Subtitle = "Pig Latin Wikipedia Page",
                    Images = cardImages,
                    Buttons = cardButtons
                };

                Attachment plAttachment = plCard.ToAttachment();
                replyToConversation.Attachments.Add(plAttachment);

                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
                //var reply = await connector.Conversations.ReplyToActivityAsync(replyToConversation);
                if (reply != null)
                    sb.AppendLine(reply.Message);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }
            return message.CreateReply(translateToPigLatin("Completed MessageTypesTest"));
        }

        private async Task<Activity> dataTypesTest(Activity message, ConnectorClient connector)
        {

            StateClient pigLatinStateClient = new StateClient(new Uri(message.ChannelId == "emulator" ? message.ServiceUrl : "https://intercom-api-scratch.azurewebsites.net"), new Microsoft.Bot.Connector.MicrosoftAppCredentials());
            //StateClient sc = new StateClient(new Microsoft.Bot.Connector.MicrosoftAppCredentials());
            BotState botState = new BotState(pigLatinStateClient);
            StringBuilder sb = new StringBuilder();
            // DM a user 
            DateTime timestamp = DateTime.UtcNow;

            pigLatinBotUserData addedUserData = new pigLatinBotUserData();
            BotData botData = new BotData();
            try
            {
                botData = (BotData)await botState.GetUserDataAsync(message.ChannelId, message.From.Id);
            }
            catch (Exception e)
            {
                if (e.Message == "Resource not found")
                { }
                else
                    throw e;
            }

            if (botData == null)
                botData = new BotData(eTag: "*");

            addedUserData = botData.GetProperty<pigLatinBotUserData>("v1") ?? new pigLatinBotUserData();

            addedUserData.isNewUser = false;
            addedUserData.lastReadLegalese = timestamp;

            try
            {
                botData.SetProperty("v1", addedUserData);
                var response = await botState.SetUserDataAsync(message.ChannelId, message.From.Id, botData);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
            }

            try
            {
                botData = (BotData)await botState.GetUserDataAsync(message.ChannelId, message.From.Id);
            }
            catch (Exception e)
            {
                if (e.Message == "Resource not found")
                { }
                else
                    throw e;
            }

            if (botData == null)
                botData = new BotData(eTag: "*");

            addedUserData = botData.GetProperty<pigLatinBotUserData>("v1") ?? new pigLatinBotUserData();

            if (addedUserData.isNewUser != false || addedUserData.lastReadLegalese != timestamp)
                sb.Append(translateToPigLatin("Bot data didn't match doofus."));
            else
                sb.Append(translateToPigLatin("Yo, that get/save/get thing worked."));

            return message.CreateReply(sb.ToString(),"en-Us");

        }
    }
}

