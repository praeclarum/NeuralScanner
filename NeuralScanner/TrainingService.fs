namespace NeuralScanner

open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.IO
open System.Numerics
open System.Globalization

open SceneKit
open MetalTensors


type TrainingService (project : Project) =

    // Hyperparameters
    let outputScale = ProjectDefaults.outputScale
    let samplingDistance = ProjectDefaults.samplingDistance
    let lossClipDelta = ProjectDefaults.lossClipDelta
    let networkDepth = ProjectDefaults.networkDepth
    let networkWidth = ProjectDefaults.networkWidth
    let batchSize = ProjectDefaults.batchSize
    let useTanh = ProjectDefaults.useTanh

    let numEpochs = 1_000

    let weightsInit = WeightsInit.Default

    // State
    let dataDir = project.CaptureDirectory

    let changed = Event<_> ()
    let batchTrained = Event<_> ()

    let mutable training : Threading.CancellationTokenSource option = None

    let reportError (e : exn) =
        printfn "ERROR: %O" e

    let createSdfModel () =
        let input = Tensor.Input("xyzw", 4)
        let hiddenLayer (x : Tensor) (i : int) (drop : bool) =
            let r = x.Dense(networkWidth, weightsInit=weightsInit, name=sprintf "hidden%d" i).ReLU(sprintf "relu%d" i)
            //if drop then r.Dropout(dropoutRate, name=sprintf "drop%d" i) else r
            r
        let houtput1 =
            (seq{0..(networkDepth/2-1)}
             |> Seq.fold (fun a i -> hiddenLayer a i true) input)
        let inner = houtput1.Concat(input, name="skip")
        let houtput =
            (seq{networkDepth/2..(networkDepth-1)}
             |> Seq.fold (fun a i -> hiddenLayer a i (i + 1 < networkDepth)) inner)
        let output = houtput.Dense(1, weightsInit=weightsInit, name="raw_distance")
        let output = if useTanh then output.Tanh ("distance") else output
        let model = Model (input, output, "SDF")
        //let r = model.Compile (Loss.MeanAbsoluteError,
        //                       new AdamOptimizer(learningRate))
        printfn "%s" model.Summary
        model

    let optimizer = new AdamOptimizer (project.Settings.LearningRate)

    let createTrainingModel (sdfModel : Model) : Model =
        let inputXyz = Tensor.Input("xyzw", 4)
        let inputFreespace = Tensor.Input("freespace", 1)
        let inputExpected = Tensor.Input("distance", 1)
        let output = sdfModel.Call(inputXyz)
        let model = Model ([|inputXyz; inputFreespace; inputExpected|], [|output|], "TrainSDF")

        let clipOutput = output.Clip(-lossClipDelta, lossClipDelta)
        let clipExpected = inputExpected.Clip(-lossClipDelta, lossClipDelta)

        let surfaceLoss = clipOutput.Loss(clipExpected, Loss.MeanAbsoluteError)

        let freespaceLoss = (0.0f - clipOutput).Clip(0.0f, lossClipDelta)

        let totalLoss = surfaceLoss * (1.0f - inputFreespace) + freespaceLoss * inputFreespace

        model.AddLoss (totalLoss)

        try
            model.Compile (optimizer) |> ignore
        with ex ->
            reportError ex

        printfn "%s" model.Summary
        model

    let mutable data = lazy SdfDataSet (project, samplingDistance, outputScale)

    //let modelPath = dataDir + "/Model.zip"
    let trainingModelPath = dataDir + "/TrainingModel.zip"

    let loadTrainingModel () =
        try
            if File.Exists trainingModelPath then
                //let fileSize = (new FileInfo(modelPath)).Length
                let model = Model.Load (trainingModelPath)
                try
                    model.Compile (optimizer) |> ignore
                with ex ->
                    reportError ex
                model
            else
                createTrainingModel (createSdfModel ())
        with ex ->
            reportError ex
            createTrainingModel (createSdfModel ())

    let mutable trainingModelO : Model option = None

    let getTrainingModel () =
        match trainingModelO with
        | Some x -> x
        | None ->
            let x = loadTrainingModel ()
            trainingModelO <- Some x
            x

    let getModel () =
        let tmodel = getTrainingModel ()
        let model = tmodel.Submodels.[0]
        model

    let losses = ResizeArray<float32> ()

    let newTrainingId () =
        DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
    let mutable trainingId = newTrainingId ()

    member this.Losses = losses.ToArray ()

    member this.BatchTrained = batchTrained.Publish

    member this.Changed = changed.Publish

    member this.IsTraining = training.IsSome

    member this.TrainingId = trainingId

    member this.SnapshotId = sprintf "%s_%d" this.TrainingId project.Settings.TotalTrainedPoints

    member this.Model = getModel ()
    member this.Data = data.Value

    member private this.Train (cancel : Threading.CancellationToken) =
        //let data = SdfDataSet ("/Users/fak/Data/NeuralScanner/Onewheel")
        //let struct(inputs, outputs) = data.GetRow(0, null)
        let mutable ntrained = 0

        let startSeconds = project.Settings.TotalTrainedSeconds
        let stopwatch = new Diagnostics.Stopwatch ()
        stopwatch.Start ()

        try
            let trainingModel = getTrainingModel ()
            printfn "%O" trainingModel
            let dataSource = data.Value
            dataSource.WaitForRegistration ()
            let numPointsPerEpoch = dataSource.Count
            let callback (h : TrainingHistory.BatchHistory) =
                //printfn "LOSS %g" h.AverageLoss
                h.ContinueTraining <- not cancel.IsCancellationRequested
                ntrained <- ntrained + h.BatchSize
                project.Settings.TotalTrainedPoints <- project.Settings.TotalTrainedPoints + h.BatchSize
                project.Settings.TotalTrainedSeconds <- startSeconds + (int stopwatch.Elapsed.TotalSeconds)
                let loss = h.AverageLoss
                losses.Add (loss)
                batchTrained.Trigger (batchSize, numPointsPerEpoch, loss, dataSource.PopLastTrainingData ())
            optimizer.LearningRate <- project.Settings.LearningRate
            //this.GenerateMesh ()
            let mutable epoch = 0
            while not cancel.IsCancellationRequested && epoch < numEpochs do
                let history = trainingModel.Fit (dataSource, batchSize = batchSize, epochs = 1.0f, callback = fun h -> callback h)
                epoch <- epoch + 1
        with ex ->
            reportError ex
        stopwatch.Stop ()
        if ntrained > 0 then
            this.SaveModel ()
            project.SetModified "Settings"

    member this.SaveModel () =
        match trainingModelO with
        | None -> ()
        | Some trainingModel ->
            let tmpPath = IO.Path.GetTempFileName ()
            trainingModel.SaveArchive (tmpPath)
            if File.Exists (trainingModelPath) then
                File.Delete (trainingModelPath)
            IO.File.Move (tmpPath, trainingModelPath)
            printfn "SAVED MODEL: %s" trainingModelPath

    member this.Run () =
        match training with
        | Some _ -> ()
        | None ->
            let cts = new Threading.CancellationTokenSource ()
            training <- Some cts
            changed.Trigger "IsTraining"
            async {
                this.Train (cts.Token)
            }
            |> Async.Start

    member this.Pause () =
        match training with
        | None -> ()
        | Some cts ->
            training <- None
            cts.Cancel ()
            changed.Trigger "IsTraining"

    member this.Reset () =
        try
            if File.Exists trainingModelPath then
                File.Delete trainingModelPath
            trainingModelO <- None
            data <- lazy SdfDataSet (project, samplingDistance, outputScale)
            trainingId <- newTrainingId ()
            project.Settings.TotalTrainedPoints <- 0
            project.Settings.TotalTrainedSeconds <- 0
            project.SetModified "Settings"
        with ex ->
            reportError ex

module TrainingServices =
    let private services = ConcurrentDictionary<string, TrainingService> ()
    let getForProject (project : Project) : TrainingService =
        let key = project.ProjectDirectory
        match services.TryGetValue key with
        | true, x -> x
        | _ ->
            let s = TrainingService (project)
            if services.TryAdd (key, s) then
                s
            else
                services.[key]


