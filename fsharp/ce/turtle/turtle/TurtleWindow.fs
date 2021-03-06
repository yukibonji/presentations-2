﻿// ----------------------------------------------------------------------------------------------
// Copyright (c) Mårten Rånge.
// ----------------------------------------------------------------------------------------------
// This source code is subject to terms and conditions of the Microsoft Public License. A
// copy of the license can be found in the License.html file at the root of this distribution.
// If you cannot locate the  Microsoft Public License, please send an email to
// dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound
//  by the terms of the Microsoft Public License.
// ----------------------------------------------------------------------------------------------
// You must not remove this notice, or any other, from this software.
// ----------------------------------------------------------------------------------------------

namespace TurtlePower

open System
open System.Diagnostics
open System.Threading
open System.Collections.Generic

open SharpDX
open TurtlePower

module TurtleWindow =

    type Line =
        {
            Color   : Turtle.Color
            Width   : float32
            From    : Vector2
            To      : Vector2
        }
        static member New c w f t = {Color = c; Width = w; From = f; To = t}

    type TurtleMessage =
        {
            Turtle  : unit -> List<Line>
            Reply   : List<Line> -> unit
        }
        static member New t r = {Turtle = t; Reply = r;}

    let Show (turtleGenerator : float32 -> Turtle.Movement<unit>) =
        let turtleExecutor (t : Turtle.Movement<unit>) : List<Line> =
            let lines = List<Line> (64)
            ignore <| Turtle.Execute
                Turtle.Brown
                3.F
                (NewVector2 0.F 0.F)
                (NewVector2 0.F 1.F)
                (fun c w f t -> lines.Add <| Line.New c w f t)
                t
            lines

        let turtleProcessor (ct : CancellationToken) (input : MailboxProcessor<TurtleMessage>) : Async<unit> =
            async {
                while not ct.IsCancellationRequested do
                    let! message = input.Receive ()
                    let lines = message.Turtle ()
                    message.Reply lines
            }

        let sw = Stopwatch ()
        sw.Start ()

        use form                = new Windows.RenderForm ("Turtle Power")

        form.ClientSize         <- System.Drawing.Size (1400,1100)

        let device              = ref <| new Device (form)

        let disposeDevice ()    = TryRun (upcast !device : IDisposable).Dispose
        let recreateDevice ()   = disposeDevice ()
                                  device := new Device (form)

        use onExitDisposeDevice = OnExit disposeDevice

        use cts = new CancellationTokenSource ()
        let ct = cts.Token

        let turtle = turtleGenerator 0.F
        let currentLines = ref <| turtleExecutor turtle

        use mp = MailboxProcessor.Start (turtleProcessor ct, ct)

        use onExitCancelTask    = OnExit cts.Cancel

        let resizer             = EventHandler (fun o e -> recreateDevice ())

        form.Resize.AddHandler  resizer

        use onExitRemoveHandler = OnExit <| fun () -> form.Resize.RemoveHandler resizer

        Windows.RenderLoop.Run (form, fun () ->

            let turtle = turtleGenerator <| float32 sw.Elapsed.TotalSeconds
            mp.Post <| TurtleMessage.New (fun () -> turtleExecutor turtle) (fun lines -> currentLines := lines)

            let d = !device
            let colors              =   [
                                            Turtle.Brown            , d.BrownBrush
                                            Turtle.LimeGreen        , d.LimeGreenBrush
                                            Turtle.Lime             , d.LimeGreenBrush
                                            Turtle.MediumVioletRed  , d.MediumVioletRedBrush
                                        ]
                                        |> List.fold (fun s (c,b) -> s |> Map.add c b) Map.empty

            let lines = !currentLines

            d.Draw <| fun d2dRenderTarget ->

                d2dRenderTarget.Clear (Nullable<_> (Color.White.ToColor4 ()))

                let transform =
                    Matrix3x2.Identity
                    <*> Matrix3x2.Rotation (Deg2Rad * 180.F)
                    <*> Matrix3x2.Translation (d.Width/2.F, d.Height - 20.F)
                d2dRenderTarget.Transform <- transform

                let c = lines.Count
                for i in 0..c - 1 do
                    let l = lines.[i]
                    d2dRenderTarget.DrawLine (l.From, l.To, colors.[l.Color], l.Width)
                )






