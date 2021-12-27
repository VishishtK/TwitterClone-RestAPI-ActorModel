#r "nuget: Akka.Fsharp"
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"
#r "nuget: Suave"
#r "nuget: FSharp.Json"
#r "nuget: FSharp.Data"

open System
open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open System.Collections.Generic
open System.Text.RegularExpressions
open FSharp.Json

open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Suave.ServerErrors
open Suave.Writers

type Message = {
    Action: String;
    User: String;
    Tweet: String;
    TweetId: String;
    ReTweetId: String;
    Mentions: String;
    HashTags: String;
    ToSubscribe: String
    Online: Boolean
}

type Response = {
    Action: String;
    Message : String;
    Success : String
}

let defaultMessage = {Action=" "; User=" "; Tweet=" "; TweetId=" "; ReTweetId=" "; Mentions= "false"; HashTags=" "; ToSubscribe=" "; Online = true}
let defaultResponse = {Action=" "; Message=" "; Success= "true"}

let system = ActorSystem.Create("twitterServer")

// TODO Combine usersSocket and onlineStatus
let usersSocket = new Dictionary<string, WebSocket>()
let users = new List<string>()
let onlineStatus = new Dictionary<string,Boolean>()
let userTweets = new Dictionary<string, List<Message>>()
let tweets = new Dictionary<string, Message>()
let subscriptions = new Dictionary<string, List<String>>()
let subscribers = new Dictionary<string, List<String>>()
let hashTagMapping = new Dictionary<string, List<Message>>()
let mentionMap = new Dictionary<string, List<Message>>()
let feed = new Dictionary<string,List<Message>>()

let twitterServer(mailbox: Actor<_>) =
    let rec loop()= actor{
        let! (msg:Response*WebSocket) = mailbox.Receive();
        let response,socket = msg
        let byteResponse =
            Json.serialize(response)
            |> System.Text.Encoding.ASCII.GetBytes
            |> ByteSegment
        Async.RunSynchronously(socket.send Text byteResponse true) |> ignore
        return! loop()
    }
    loop ()

let twitterServerRef = spawn system "twitterServer" twitterServer

let processHashTag tweet hashTag = 
    let containsHashTag,list = hashTagMapping.TryGetValue(hashTag)
    if containsHashTag
    then
        list.Add(tweet)
    else
        let newList = List [tweet]
        hashTagMapping.Add(hashTag,newList)

let processMention tweet mention = 
    printfn "level 1 %s" mention
    let containsMention,list = mentionMap.TryGetValue(mention)
    if containsMention
    then
        printfn "%s" mention
        let userOnline, socket = usersSocket.TryGetValue(mention)
        if userOnline then
            twitterServerRef <! ({defaultResponse with Action = "Tagged in Tweet"; Message = tweet.User + " tweeted and mentioned you " + tweet.Tweet},socket)
        list.Add(tweet)

let processTweet tweet =
    let containsUser, list = userTweets.TryGetValue(tweet.User)
    if containsUser
    then
        list.Add(tweet)
        let words = tweet.Tweet.Split [|' '|]
        for word in words do
            if Regex.IsMatch(word, "^(@)([a-zA-Z])+")
            then
                processMention tweet (word.Split[|'@'|]).[1]
            else if Regex.IsMatch(word, "^(#)([a-zA-Z0-9])+")
            then
                processHashTag tweet word
        true           
    else
        false

let publishTweet tweet = 
    let subsribersExist, subscribersList = subscribers.TryGetValue(tweet.User)
    if subsribersExist then
        for subscriber in subscribersList do
            let feedExists, userFeed = feed.TryGetValue(subscriber)
            userFeed.Add(tweet)
            let userOnline, socket = usersSocket.TryGetValue(subscriber)
            twitterServerRef <! ({defaultResponse with Action = "Tweet"; Message = tweet.User + " tweeted " + tweet.Tweet},socket)

let ws (webSocket : WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true

        while loop do
            let! msg = webSocket.read()

            match msg with
            | (Text, data, true) ->
                let msg:Message = Json.deserialize(UTF8.toString data)
                if msg.Action = "login" then
                    let feedExists, userFeed = feed.TryGetValue(msg.User)
                    let userOnline, socket = usersSocket.TryGetValue(msg.User)
                    if (feedExists && userOnline)
                    then 
                        printfn "Message received login 2"
                        for tweet in userFeed do
                            printfn "Message received login 4"
                            twitterServerRef <! ({defaultResponse with Action = "Feed"; Message = tweet.User + " tweeted " + tweet.Tweet},socket)
                    
                let userExists,socket = usersSocket.TryGetValue(msg.User)
                if userExists then
                    usersSocket.Remove(msg.User) |> ignore
                usersSocket.Add(msg.User,webSocket)
                let response = sprintf "Socket connection created"
                let byteResponse =
                    response
                    |> System.Text.Encoding.ASCII.GetBytes
                    |> ByteSegment
                do! webSocket.send Text byteResponse true
            | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
                loop <- false

            | _ -> ()
    }

let processApi (rawForm: byte[]) =
    let message:Message = Json.deserialize (System.Text.Encoding.UTF8.GetString(rawForm))
    message

let register request = 
    printfn "Message received"
    let msg:Message =  request
    if not(users.Contains(msg.User))
    then 
        users.Add(msg.User)
        userTweets.Add(msg.User,new List<Message>())
        mentionMap.Add(msg.User,new List<Message>())
        feed.Add(msg.User,new List<Message>())
        subscribers.Add(msg.User,new List<String>())
        subscriptions.Add(msg.User,new List<String>())
        printfn "Message received and registered"
        "Registered"
    else
        "Already Registered"

let login request =
    let msg:Message =  request
    printfn "Message received login"
    let feedExists, userFeed = feed.TryGetValue(msg.User)
    let userOnline, socket = usersSocket.TryGetValue(msg.User)
    if (feedExists && userOnline)
    then 
        "Loggedin"
    else
        printfn "Message received login 3"
        "Register First"


let mutable tweetID = 0 
let tweet request = 
    let msg:Message =  request
    let msgWithID = {msg with TweetId = msg.User+"-"+(string)tweetID}
    tweetID <- tweetID + 1
    if processTweet msgWithID
    then
        tweets.Add(msgWithID.TweetId,msgWithID)
        publishTweet msgWithID
        Json.serialize{defaultResponse with Action = "Tweet"; Message = "Tweeted the message"}
    else
        Json.serialize{defaultResponse with Action = "Tweet"; Message = "Some Error Occured"; Success = "false"}

let follow request = 
    let msg:Message =  request
    let subscriberExist = users.Contains(msg.User)
    let publisherExist = users.Contains(msg.ToSubscribe)
    if subscriberExist&&publisherExist then
        let publisherExist, publishersList = subscribers.TryGetValue(msg.ToSubscribe)
        let subscriptionsExist, subscriptionsList = subscriptions.TryGetValue(msg.User)
        publishersList.Add(msg.User)
        subscriptionsList.Add(msg.ToSubscribe)
        Json.serialize{defaultResponse with Action = "Follow"; Message = ("Followed" + msg.ToSubscribe);}
    else
        Json.serialize{defaultResponse with Action = "Follow"; Message = "Some Error Occured"; Success = "false"}

let retweet request = 
    let msg:Message = request
    let tweetExists, tweetMsg = tweets.TryGetValue(msg.TweetId)
    printfn "%s" msg.TweetId
    if tweetExists then
        tweet {tweetMsg with User = msg.User; ReTweetId = msg.TweetId}
    else
        Json.serialize{defaultResponse with Action = "ReTweet"; Message = "Tweet does not exist"; Success = "false"}

let query request = 
    let msg:Message = request
    let userOnline, socket = usersSocket.TryGetValue(msg.User)
    if msg.Mentions = "true"
    then
        let mentionsExist, mentionsList = mentionMap.TryGetValue(msg.User)
        for tweet in mentionsList do
            twitterServerRef <! ({defaultResponse with Action = "Query"; Message = (" TweetID:"+tweet.TweetId+" Tweet: "+ tweet.Tweet)},socket)
    if msg.HashTags.Length <> 0 then
        let hashTagExists, hashTagList = hashTagMapping.TryGetValue(msg.HashTags)
        if hashTagExists then
            for tweet in hashTagList do
                twitterServerRef <! ({defaultResponse with Action = "Query"; Message = (" TweetID:"+tweet.TweetId+" Tweet: "+ tweet.Tweet)},socket)
    "Query Recieved"

let handleRequest method = 
    let req = request (fun req ->
                        req.rawForm
                        |> processApi
                        |> method
                        |> OK
                    )
    req >=> setMimeType "application/json"


let app = 
  choose [
        path "/connect" >=> handShake ws
        path "/register" >=> handleRequest register
        path "/tweet" >=> handleRequest tweet
        path "/retweet" >=> handleRequest retweet
        path "/follow" >=> handleRequest follow
        path "/query" >=> handleRequest query
        path "/login" >=> handleRequest login
    ]

startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app

Console.ReadLine() |> ignore