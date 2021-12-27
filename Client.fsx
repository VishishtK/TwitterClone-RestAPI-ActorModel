#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: FSharp.Json"
#r "nuget: FSharp.Data"

open System
open System.IO
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open FSharp.Data
open FSharp.Data.HttpRequestHeaders

open Akka.FSharp
open Akka.Actor
open Akka.Configuration
open FSharp.Json
open System.Collections.Generic

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

let ws = new ClientWebSocket()
let uri = new Uri("ws://localhost:8080/connect")
let cts = new CancellationToken()
let ctask = ws.ConnectAsync(uri,cts)
while not (ctask.IsCompleted) do()

let rec receive() =
    async{
        let rsegment = new ArraySegment<Byte>(Array.create(500) Byte.MinValue)
        let task = ws.ReceiveAsync(rsegment, cts)
        while not (task.IsCompleted) do
            ()
        let resp = System.Text.Encoding.ASCII.GetString (rsegment.Array)
        printfn "\n================ Websocket Response :  %s" resp
        return! receive()
    }
Async.Start(receive(), (new CancellationTokenSource()).Token)

let register data =
    let json = Json.serialize data
    Http.Request("http://127.0.0.1:8080/register",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json)

let login data =
    let json = Json.serialize data
    Http.Request("http://127.0.0.1:8080/login",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json)

let tweet data = 
    let json = Json.serialize data
    Http.Request("http://127.0.0.1:8080/tweet",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json)

let retweet data = 
    let json = Json.serialize data
    Http.Request("http://127.0.0.1:8080/retweet",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json)

let follow data = 
    let json = Json.serialize data
    Http.Request("http://127.0.0.1:8080/follow",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json)

let query data = 
    let json = Json.serialize data
    Http.Request("http://127.0.0.1:8080/query",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json)

let startWebSocket (data:string) =
    let buffer = System.Text.Encoding.ASCII.GetBytes data
    let segment = new ArraySegment<byte>(buffer)
    ws.SendAsync(segment, WebSocketMessageType.Text, true, cts) |> ignore


printfn "Login/Register?"
let action = Console.ReadLine()
// let action = "Register"
printfn "UserID:"
let userID = Console.ReadLine()
// let userID = "Vishy"

let defaultMessage = {Action=" "; User=userID; Tweet=" "; TweetId=" "; ReTweetId=" "; Mentions= "false"; HashTags=" "; ToSubscribe=" "; Online = true}
let defaultResponse = {Action=" "; Message=" "; Success= "true"}

if(action = "login") then
    printfn "login started"
    let response = login {defaultMessage with Action="Login"}
    if (response.StatusCode = 200) then
        startWebSocket (Json.serialize {defaultMessage with Action = "login"; User = userID})
else
    let response = register {defaultMessage with Action="Register"}
    if (response.StatusCode = 200) then
        startWebSocket (Json.serialize {defaultMessage with Action = "Regsiter"; User = userID})

while true do
    printfn "Input Command"
    let message = Console.ReadLine()
    let command = message.Split(',')
    if (command.[0] = "Retweet") then
        let response = retweet {defaultMessage with Action = "Tweet"; TweetId = command.[1]}
        if (response.StatusCode = 200) then
            printfn "%A"  response.Body

    elif (command.[0] = "Tweet") then
        let response = tweet {defaultMessage with Action = "Tweet"; Tweet = command.[1]}
        if (response.StatusCode = 200) then
            printfn "%A"  response.Body

    elif (command.[0] = "Follow") then
        let response = follow {defaultMessage with Action = "Follow"; ToSubscribe = command.[1]}
        if (response.StatusCode = 200) then
            printfn "%A"  response.Body

    elif (command.[0] = "SearchTag") then
        let response = query {defaultMessage with Action = "Query"; Mentions ="true"}
        if (response.StatusCode <> 200) then
            printfn "%A"  response.Body

    elif (command.[0] = "SearchHashTag") then
        let response = query {defaultMessage with Action = "Query"; HashTags = command.[1]}
        if (response.StatusCode <> 200) then
            printfn "%A"  response.Body


Console.ReadLine() |> ignore