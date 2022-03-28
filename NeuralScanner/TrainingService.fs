namespace NeuralScanner

open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.IO
open System.Numerics
open System.Globalization

open MetalTensors

type TrainingService (project : Project) =

    let dataDir = project.CaptureDirectory

    let changed = Event<_> ()

    let mutable training = false

    // Hyperparameters
    let outputScale = 200.0f
    let samplingDistance = 1.0e-3f
    let lossClipDelta = 1.0e-2f * outputScale
    let learningRate = 1.0e-6f * outputScale
    let networkDepth = 8
    let networkWidth = 512
    let batchSize = 1024
    let useTanh = false
    //let dropoutRate = 0.2f

    // Derived parameters

    let batchTrained = Event<_> ()

    let weightsInit = WeightsInit.Default//.GlorotUniform (0.54f) //.Uniform (-0.02f, 0.02f)

    let createSdfModel () =
        let input = Tensor.Input("xyz", 3)
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

    let createTrainingModel (sdfModel : Model) : Model =
        let inputXyz = Tensor.Input("xyz", 3)
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
        let r = model.Compile (new AdamOptimizer (learningRate))
        printfn "%s" model.Summary
        model

    let mutable data : SdfDataSet option = None
    let getData () =
        match data with
        | Some x -> x
        | None ->
            let d = SdfDataSet (dataDir, samplingDistance, outputScale)
            data <- Some d
            d

    let modelPath = dataDir + "/Model.zip"

    let model = 
        if false && File.Exists modelPath then
            let fileSize = (new FileInfo(modelPath)).Length
            let model = Model.Load (modelPath)
            let r = model.Compile (Loss.MeanAbsoluteError,
                                    new AdamOptimizer(learningRate))
            model
        else
            createSdfModel ()

    let trainingModel = createTrainingModel model

    let mutable trainedPoints = 0

    let losses = ResizeArray<float32> ()

    member this.Losses = losses.ToArray ()

    member this.BatchTrained = batchTrained.Publish

    member this.Train () =
        //let data = SdfDataSet ("/Users/fak/Data/NeuralScanner/Onewheel")
        //let struct(inputs, outputs) = data.GetRow(0, null)
        printfn "%O" trainingModel
        let data = getData ()
        //this.GenerateMesh ()
        let mutable totalTrained = 0
        let numEpochs = 10
        let callback (h : TrainingHistory.BatchHistory) =
            //printfn "LOSS %g" h.AverageLoss
            totalTrained <- batchSize + totalTrained
            let progress = float32 totalTrained / float32 (numEpochs * data.Count)
            let loss = h.AverageLoss
            losses.Add (loss)
            batchTrained.Trigger (progress, totalTrained, loss)
            ()
        for epoch in 0..(numEpochs-1) do
            let history = trainingModel.Fit(data, batchSize = batchSize, epochs = 1.0f, callback = fun h -> callback h)
            trainedPoints <- trainedPoints + data.Count
            this.GenerateMesh ()
        ()

    member this.Save () =
        trainingModel.SaveArchive (modelPath)
        ()

    member this.GenerateMesh () =

        let nx, ny, nz = 64, 64, 64
        let mutable numPoints = 0
        let totalPoints = nx*ny*nz

        let data = getData ()

        let sdf (x : Memory<Vector3>) (y : Memory<Vector4>) =
            let batchTensors = Array.init x.Length (fun i ->
                let x = x.Span
                let p = x.[i] - data.VolumeCenter
                let input = Tensor.Array(p.X, p.Y, p.Z)
                [|input|])
            let results = model.Predict(batchTensors)
            let y = y.Span
            for i in 0..(x.Length-1) do
                let r = results.[i].[0]
                let yvec = Vector4(1.0f, 1.0f, 1.0f, r.[0])
                //if numPoints < totalPoints / 100 then
                //    printfn "%g" yvec.W
                y.[i] <- yvec
            numPoints <- numPoints + y.Length
            let progress = float numPoints / float totalPoints
            printfn "NN Sample Progress: %.1f" (progress * 100.0)

        let voxels = SdfKit.Voxels.SampleSdf (sdf, data.VolumeMin, data.VolumeMax, nx, ny, nz, batchSize = batchSize, maxDegreeOfParallelism = 2)
        voxels.ClipToBounds ()
        let mesh = SdfKit.MarchingCubes.CreateMesh (voxels, 0.0f, step = 1)
        mesh.WriteObj (dataDir + sprintf "/Onewheel_s%d_d%d_c%d_%s_l%d_%d.obj" (int outputScale) (int (1.0f/samplingDistance)) (int (1.0f/lossClipDelta)) (if useTanh then "tanh" else "n") (int (1.0f/learningRate)) trainedPoints)
        ()

    member this.Changed = changed.Publish

    member this.IsTraining = training

    member this.Run () =
        if not training then
            training <- true
            changed.Trigger "IsTraining"
            async {
                this.Train ()
            }
            |> Async.Start

    member this.Pause () =
        if training then
            training <- false
            changed.Trigger "IsTraining"


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


