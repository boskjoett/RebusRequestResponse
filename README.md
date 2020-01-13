# Description

Two simple .NET Core console applications demonstrating how to perform request-responses in Rebus.

The Requester App sends the requests using either bus.Publish or bus.SendRequest (using Rebus.Async)

The Responder App reponds to requests using bus.Reply.
