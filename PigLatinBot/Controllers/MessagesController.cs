using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Azure;
using System.Collections.Generic;
using Microsoft.Rest;
using System.Diagnostics;
using System.Configuration;
using System.Text;
using Microsoft.ApplicationInsights;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Web.Http.Controllers;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.Bot.Builder.Dialogs.Internals;

namespace PigLatinBot
{
    public class pigLatinBotUserData
    {
        public bool isNewUser = true;
        public DateTime lastReadLegalese = DateTime.MinValue;
    }

    //[BotAuthentication]
    [BotAuthentication(OpenIdConfigurationUrl = "https://intercom-api-scratch.azurewebsites.net/v1/.well-known/openidconfiguration")]
    public class MessagesController : ApiController
    {
        private CloudStorageAccount storageAccount;
        private TableBotDataStore botStateStore;
        private DateTime lastModifiedPolicies = DateTime.Parse("2015-10-01");


        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and reply to it, either directly or as an async delayed response
        /// </summary>
        /// <param name="message"></param>
        [ResponseType(typeof(Microsoft.Bot.Connector.Activity))]
        public virtual async Task<HttpResponseMessage> Post([FromBody] Microsoft.Bot.Connector.Activity message) 
        {
            ConnectorClient connector = new ConnectorClient(new Uri(message.ServiceUrl));

            IBotDataStore<BotData> dataStore = botStateStore;
            storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("piglatinbotsjameslew_AzureStorageConnectionString"));
            botStateStore = new TableBotDataStore(storageAccount, "botdata");

            Microsoft.Bot.Connector.Activity replyMessage = message.CreateReply();
            replyMessage.Locale = "en-Us";
            replyMessage.TextFormat = TextFormatTypes.Plain;

            if (message.GetActivityType() != ActivityTypes.Message)
            {
                replyMessage = await handleSystemMessagesAsync(message, connector, botStateStore);
                if (replyMessage != null)
                {
                    var reply = await connector.Conversations.ReplyToActivityAsync(replyMessage);
                }
            }
            else
            {
                if (message.Text.Contains("MessageTypesTest"))
                {
                    Microsoft.Bot.Connector.Activity mtResult = await messageTypesTest(message, connector);
                    await connector.Conversations.ReplyToActivityAsync(mtResult);
                }
                else if (message.Text.Contains("DataTypesTest"))
                {
                    Microsoft.Bot.Connector.Activity dtResult = await dataTypesTest(message, connector, botStateStore);
                    await connector.Conversations.ReplyToActivityAsync(dtResult);
                }
                else if (message.Text.Contains("CardTypesTest"))
                {
                    Microsoft.Bot.Connector.Activity ctResult = await cardTypesTest(message, connector);
                    await connector.Conversations.ReplyToActivityAsync(ctResult);
                }

                try
                {

                    if(await isNewUser(message.From.Id, message, botStateStore))
                    {
                        Microsoft.Bot.Connector.Activity introMessage = message.CreateReply();
                        introMessage.Locale = "en-Us";
                        introMessage.TextFormat = TextFormatTypes.Plain;
                        introMessage.InputHint = InputHints.IgnoringInput;

                        introMessage.Text = string.Format(translateToPigLatin("Hey there, I'm PigLatinBot. I make intelligible text unintelligible.  Ask me how by typing 'Help', and for terms and info, click ") + "[erehay](http://www.piglatinbot.com)", message.From.Name);
                        var reply = await connector.Conversations.ReplyToActivityAsync(introMessage);
                    }
                    replyMessage.InputHint = InputHints.AcceptingInput;
                    replyMessage.Speak = message.Text;
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
            var responseOtherwise = Request.CreateResponse(HttpStatusCode.OK);
            return responseOtherwise;
        }
        
        private async Task<bool> isNewUser(string userId, Microsoft.Bot.Connector.Activity message, TableBotDataStore botStateStore)
        {
            IBotDataStore<BotData> dataStore = botStateStore;
            pigLatinBotUserData addedUserData = new pigLatinBotUserData();
            BotData botData = new BotData();

            try
            {
                botData = (BotData)await dataStore.LoadAsync(new Address(message.Recipient.Id, message.ChannelId, userId, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, default(CancellationToken));
            }
            catch (Exception e)
            {
                if (e.Message == "Resource not found")
                { }
                else
                    throw e;
            }

            if (botData.Data == null)
                botData = new BotData(eTag: "*");

            addedUserData = botData.GetProperty<pigLatinBotUserData>("v1") ?? new pigLatinBotUserData();

            if (addedUserData.isNewUser == true)
            {
                return true;
            }
            else return false;
        }

        private async Task<Microsoft.Bot.Connector.Activity> handleSystemMessagesAsync(Microsoft.Bot.Connector.Activity message, ConnectorClient connector, TableBotDataStore botStateStore)
        {
            Microsoft.Bot.Connector.Activity replyMessage = message.CreateReply();
            message.Locale = "en";
            IBotDataStore<BotData> dataStore = botStateStore;

            switch (message.GetActivityType())
            {
                case ActivityTypes.DeleteUserData:
                    //In this case the DeleteUserData message comes from the user so we can clear the data and set it back directly

                    BotData currentBotData = (BotData)await dataStore.LoadAsync(new Address(message.Recipient.Id, message.ChannelId, message.From.Id, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, default(CancellationToken));
                    pigLatinBotUserData deleteUserData = new pigLatinBotUserData();
                    currentBotData.SetProperty("v1", deleteUserData);
                    await dataStore.SaveAsync(new Address(message.Recipient.Id, message.ChannelId, message.From.Id, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, currentBotData, default(CancellationToken));
                    
                    replyMessage.Text = translateToPigLatin("I have deleted your data oh masterful one");
                    Trace.TraceInformation("Clearing user's BotUserData");
                    return replyMessage;

                //if they're new or haven't seen the updated legal documents, send them a message
                //use the incoming message to set up the outgoing message
                case ActivityTypes.ConversationUpdate:

                    foreach(ChannelAccount added in message.MembersAdded)
                    {
                        Microsoft.Bot.Connector.Activity addedMessage = message.CreateReply();

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
                        
                        if(await isNewUser(added.Id, message, botStateStore))
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
                                await dataStore.SaveAsync(new Address(message.Recipient.Id, message.ChannelId, added.Id, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, botData, default(CancellationToken));

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
                        Microsoft.Bot.Connector.Activity removedMessage = message.CreateReply();
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

        private async Task<Microsoft.Bot.Connector.Activity> messageTypesTest(Microsoft.Bot.Connector.Activity message, ConnectorClient connector)
        {

            StringBuilder sb = new StringBuilder();
            // DM a user 
            try
            {
                Microsoft.Bot.Connector.Activity newDirectToUser = new Microsoft.Bot.Connector.Activity()
                {
                    Text = "Should go directly to user",
                    Type = "message",
                    From = message.Recipient,
                    Recipient = message.From,
                    ChannelId = message.ChannelId
                };

                var ConversationId = await connector.Conversations.CreateDirectConversationAsync(message.Recipient, message.From);
                newDirectToUser.Conversation = new ConversationAccount(id: ConversationId.Id);
                var reply = await connector.Conversations.SendToConversationAsync(newDirectToUser);
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
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply("Should go to conversation, but does not address the user that generated it");
                var bcReply = await connector.Conversations.SendToConversationAsync(replyToConversation);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to user using CreateReply
            try
            {
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply("Should go to conversation, but addressing the user that generated it");
                replyToConversation.Recipient = message.From;
                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            return message.CreateReply(translateToPigLatin("Completed MessageTypesTest"));
        }

        private async Task<Microsoft.Bot.Connector.Activity> cardTypesTest(Microsoft.Bot.Connector.Activity message, ConnectorClient connector)
        {

            StringBuilder sb = new StringBuilder();

            // reply to to everyone with a PigLatin Hero Card
            try
            {
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply(translateToPigLatin("Should go to conversation, with a fancy schmancy hero card"));
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = "message";
                replyToConversation.Attachments = new List<Attachment>();

                List<CardImage> cardImages = new List<CardImage>();
                cardImages.Add(new CardImage(url: "https://3.bp.blogspot.com/-7zDiZVD5kAk/T47LSvDM_jI/AAAAAAAAByM/AUhkdynaJ1Y/s200/i-speak-pig-latin.png"));
                cardImages.Add(new CardImage(url: "https://2.bp.blogspot.com/-Ab3oCVhOBjI/Ti23EzV3WCI/AAAAAAAAB1o/tiTeBslO6iU/s1600/bacon.jpg"));

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
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to everyone with a PigLatin Thumbnail Card
            try
            {
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply(translateToPigLatin("Should go to conversation, with a smaller, but still fancy thumbnail card"));
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = "message";
                replyToConversation.Attachments = new List<Attachment>();

                List<CardImage> cardImages = new List<CardImage>();
                cardImages.Add(new CardImage(url: "https://3.bp.blogspot.com/-7zDiZVD5kAk/T47LSvDM_jI/AAAAAAAAByM/AUhkdynaJ1Y/s200/i-speak-pig-latin.png"));

                List<CardAction> cardButtons = new List<CardAction>();

                CardAction plButton = new CardAction()
                {
                    Value = "https://en.wikipedia.org/wiki/Pig_Latin",
                    Type = "openUrl",
                    Title = "WikiPedia Page"
                };
                cardButtons.Add(plButton);

                ThumbnailCard plCard = new ThumbnailCard()
                {
                    Title = translateToPigLatin("I'm a hero card, aren't I fancy?"),
                    Subtitle = "Pig Latin Wikipedia Page",
                    Images = cardImages,
                    Buttons = cardButtons
                };

                Attachment plAttachment = plCard.ToAttachment();
                replyToConversation.Attachments.Add(plAttachment);

                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to everyone with a PigLatin Signin card
            try
            {
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply(translateToPigLatin("Should go to conversation, sign-in card"));
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = "message";
                replyToConversation.Attachments = new List<Attachment>();

                List<CardAction> cardButtons = new List<CardAction>();

                CardAction plButton = new CardAction()
                {
                    Value = "https://spott.cloudapp.net/setup?id=838303b66d9a4f4c7308fa465c5abf74",
                    Type = "signin",
                    Title = "Connect"
                };
                cardButtons.Add(plButton);

                SigninCard plCard = new SigninCard( text:"You need to authorize me to access Spotify", buttons: cardButtons);

                Attachment plAttachment = plCard.ToAttachment();
                replyToConversation.Attachments.Add(plAttachment);

                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }


            // reply to to everyone with a PigLatin Receipt Card
            try
            {
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply(translateToPigLatin("Should go to conversation, with a smaller, but still fancy thumbnail card"));
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = "message";
                replyToConversation.Attachments = new List<Attachment>();

                List<CardImage> cardImages = new List<CardImage>();
                cardImages.Add(new CardImage(url: "https://3.bp.blogspot.com/-7zDiZVD5kAk/T47LSvDM_jI/AAAAAAAAByM/AUhkdynaJ1Y/s200/i-speak-pig-latin.png"));

                List<CardAction> cardButtons = new List<CardAction>();

                CardAction plButton = new CardAction()
                {
                    Value = "https://en.wikipedia.org/wiki/Pig_Latin",
                    Type = "openUrl",
                    Title = "WikiPedia Page"
                };
                cardButtons.Add(plButton);

                ReceiptItem lineItem1 = new ReceiptItem()
                {
                    Title = translateToPigLatin("Pork Shoulder"),
                    Subtitle = translateToPigLatin("8 lbs"),
                    Text = null,
                    Image = new CardImage(url: "https://3.bp.blogspot.com/-_sl51G9E5io/TeFkYbJ2lDI/AAAAAAAAAL8/Ug_naHX6pAk/s400/porkshoulder.jpg"),
                    Price = "16.25",
                    Quantity = "1",
                    Tap = null
                };

                ReceiptItem lineItem2 = new ReceiptItem()
                {
                    Title=translateToPigLatin("Bacon"),
                    Subtitle=translateToPigLatin("5 lbs"),
                    Text=null,
                    Image=new CardImage(url: "http://www.drinkamara.com/wp-content/uploads/2015/03/bacon_blog_post.jpg"),
                    Price="34.50",
                    Quantity="2",
                    Tap= null
                };

                List<ReceiptItem> receiptList = new List<ReceiptItem>();
                receiptList.Add(lineItem1);
                receiptList.Add(lineItem2);

                ReceiptCard plCard = new ReceiptCard()
                {
                    Title = translateToPigLatin("I'm a receipt card, aren't I fancy?"),
                    Buttons = cardButtons,
                    Items = receiptList,
                    Total = "275.25", 
                    Tax = "27.52"
                };

                Attachment plAttachment = plCard.ToAttachment();
                replyToConversation.Attachments.Add(plAttachment);

                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }

            // reply to to everyone with a Carousel of three hero cards
            try
            {
                Microsoft.Bot.Connector.Activity replyToConversation = message.CreateReply(translateToPigLatin("Should go to conversation, with a fancy schmancy hero card"));
                replyToConversation.Recipient = message.From;
                replyToConversation.Type = "message";
                replyToConversation.Attachments = new List<Attachment>();

                Dictionary<string, string> cardContentList = new Dictionary<string, string>();
                cardContentList.Add("PigLatin", "https://3.bp.blogspot.com/-7zDiZVD5kAk/T47LSvDM_jI/AAAAAAAAByM/AUhkdynaJ1Y/s200/i-speak-pig-latin.png");
                cardContentList.Add("Pork Shoulder", "https://3.bp.blogspot.com/-_sl51G9E5io/TeFkYbJ2lDI/AAAAAAAAAL8/Ug_naHX6pAk/s400/porkshoulder.jpg");
                cardContentList.Add("Bacon", "http://www.drinkamara.com/wp-content/uploads/2015/03/bacon_blog_post.jpg");

                foreach(KeyValuePair<string, string> cardContent in cardContentList)
                {
                    List<CardImage> cardImages = new List<CardImage>();
                    cardImages.Add(new CardImage(url:cardContent.Value ));

                    List<CardAction> cardButtons = new List<CardAction>();

                    CardAction plButton = new CardAction()
                    {
                        Value = $"https://en.wikipedia.org/wiki/{cardContent.Key}",
                        Type = "openUrl",
                        Title = "WikiPedia Page"
                    };
                    cardButtons.Add(plButton);

                    HeroCard plCard = new HeroCard()
                    {
                        Title = translateToPigLatin($"I'm a hero card about {cardContent.Key}"),
                        Subtitle = $"{cardContent.Key} Wikipedia Page",
                        Images = cardImages,
                        Buttons = cardButtons
                    };

                    Attachment plAttachment = plCard.ToAttachment();
                    replyToConversation.Attachments.Add(plAttachment);
                }

                replyToConversation.AttachmentLayout = AttachmentLayoutTypes.Carousel;

                var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);
            }
            catch (HttpOperationException e)
            {
                Trace.TraceError("Failed to send broadcast without mention, error: {0}", e.InnerException.Message);
            }


            return message.CreateReply(translateToPigLatin("Completed CardTypesTest"));
        }

        private async Task<Microsoft.Bot.Connector.Activity> dataTypesTest(Microsoft.Bot.Connector.Activity message, ConnectorClient connector, TableBotDataStore botStateStore)
        {
            IBotDataStore<BotData> dataStore = botStateStore;
            pigLatinBotUserData deleteUserData = new pigLatinBotUserData();




            StringBuilder sb = new StringBuilder();
            // DM a user 
            DateTime timestamp = DateTime.UtcNow;

            pigLatinBotUserData addedUserData = new pigLatinBotUserData();
            BotData botData = new BotData();
            try
            {
                botData = (BotData)await dataStore.LoadAsync(new Address(message.Recipient.Id, message.ChannelId, message.From.Id, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, default(CancellationToken));
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
                await dataStore.SaveAsync(new Address(message.Recipient.Id, message.ChannelId, message.From.Id, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, botData, default(CancellationToken));

            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
            }

            try
            {
                botData = (BotData)await dataStore.LoadAsync(new Address(message.Recipient.Id, message.ChannelId, message.From.Id, message.Conversation.Id, message.ServiceUrl), BotStoreType.BotUserData, default(CancellationToken));
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

