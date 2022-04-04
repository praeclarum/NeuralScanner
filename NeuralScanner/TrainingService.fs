namespace NeuralScanner

open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.IO
open System.Numerics
open System.Globalization

open SceneKit
open MetalTensors

type SdfDataSet (project : Project, samplingDistance : float32, outputScale : float32) =
    inherit DataSet ()

    let dataDirectory = project.CaptureDirectory

    let depthFiles = project.DepthPaths

    let frames =
        depthFiles
        |> Array.map (fun x -> project.GetFrame x)
    let count = depthFiles |> Seq.sumBy (fun x -> 1)
    do if count = 0 then failwithf "No files in %s" dataDirectory
    let framesMin =
        let m = 1.0e6f*Vector3.One
        frames |> Array.fold (fun a x -> Vector3.Min (x.MinPoint, a)) m
    let framesMax =
        let m = -1.0e6f*Vector3.One
        frames |> Array.fold (fun a x -> Vector3.Max (x.MaxPoint, a)) m

    let meanCenter = project.Settings.ClipTranslation
    let volumeMin, volumeMax =
        let vmin = meanCenter - project.Settings.ClipScale
        let vmax = meanCenter + project.Settings.ClipScale
        vmin, vmax
        
    let volumeCenter = (volumeMin + volumeMax) * 0.5f

    let occupancy = AxisOccupancy.Create 16

    //do for f in frames do f.FindInBoundPoints(volumeMin, volumeMax)
    let inverseClipTransform =
        let tr = project.ClipTransform
        let tr4 = Matrix4x4(tr.M11, tr.M12, tr.M13, tr.M14,
                            tr.M21, tr.M22, tr.M23, tr.M24,
                            tr.M31, tr.M32, tr.M33, tr.M34,
                            tr.M41, tr.M42, tr.M43, tr.M44)
        let mutable itr = tr
        itr.Invert ()
        let itr4 = Matrix4x4(itr.M11, itr.M12, itr.M13, itr.M14,
                             itr.M21, itr.M22, itr.M23, itr.M24,
                             itr.M31, itr.M32, itr.M33, itr.M34,
                             itr.M41, itr.M42, itr.M43, itr.M44)
        for f in frames do
            f.SetBoundsInverseTransform (itr4, tr4, occupancy)
        itr4

    let unoccupied = occupancy.GetUnoccupied ()

    let createBatchData () : BatchTrainingData =
        {
            InsideSurfacePoints = ResizeArray<SCNVector3> ()
            OutsideSurfacePoints = ResizeArray<SCNVector3> ()
            FreespacePoints = ResizeArray<SCNVector3> ()
        }
    let mutable batchData = createBatchData ()

    member this.IsClipPointOccupied (clipPoint : Vector4) : bool =
        occupancy.IsOccupied (clipPoint.X, clipPoint.Y, clipPoint.Z)

    member this.ClipWorldPoint (worldPoint : Vector3) =
        Vector4.Transform (worldPoint, inverseClipTransform)

    member this.VolumeMin = volumeMin
    member this.VolumeMax = volumeMax
    member this.VolumeCenter = volumeCenter

    override this.Count = frames |> Array.sumBy (fun x -> x.PointCount)


    override this.GetRow (index, _) =
        let inside = (index % 2) = 0
        let fi = StaticRandom.Next(frames.Length)
        let struct (i, o) = frames.[fi].GetRow (inside, volumeCenter, samplingDistance, outputScale, unoccupied, occupancy.NumCells, batchData)
        //printfn "ROW%A D%A = %A" index inside i.[2].[0]
        struct (i, o)

    member this.PopLastTrainingData () =
        let b = batchData
        batchData <- createBatchData ()
        b


type TrainingService (project : Project) =

    let dataDir = project.CaptureDirectory

    let changed = Event<_> ()

    let mutable training : Threading.CancellationTokenSource option = None

    // Hyperparameters
    let outputScale = 200.0f
    let samplingDistance = 1.0e-3f
    let lossClipDelta = 1.0e-2f * outputScale
    //let learningRate = 1.0e-6f * outputScale
    let networkDepth = 8
    let networkWidth = 512
    let batchSize = 2*1024
    let useTanh = false
    //let dropoutRate = 0.2f

    let numEpochs = 1_000

    // Derived parameters

    let batchTrained = Event<_> ()
    let newBatchData = Event<_> ()

    let weightsInit = WeightsInit.Default//.GlorotUniform (0.54f) //.Uniform (-0.02f, 0.02f)

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

    let data = lazy SdfDataSet (project, samplingDistance, outputScale)            

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

    let mutable trainedPoints = 0

    let losses = ResizeArray<float32> ()

    let outsideDistance = lossClipDelta
    let outsideSdf = Vector4 (1.0f, 1.0f, 1.0f, outsideDistance)

    member this.Losses = losses.ToArray ()

    member this.BatchTrained = batchTrained.Publish
    member this.NewBatchData = newBatchData.Publish

    member private this.Train (cancel : Threading.CancellationToken) =
        //let data = SdfDataSet ("/Users/fak/Data/NeuralScanner/Onewheel")
        //let struct(inputs, outputs) = data.GetRow(0, null)
        let mutable totalTrained = 0

        try
            let trainingModel = getTrainingModel ()
            printfn "%O" trainingModel
            let dataSource = data.Value
            let callback (h : TrainingHistory.BatchHistory) =
                //printfn "LOSS %g" h.AverageLoss
                h.ContinueTraining <- not cancel.IsCancellationRequested
                totalTrained <- batchSize + totalTrained
                let loss = h.AverageLoss
                losses.Add (loss)
                batchTrained.Trigger (batchSize, totalTrained, loss)
                newBatchData.Trigger (dataSource.PopLastTrainingData ())
            optimizer.LearningRate <- project.Settings.LearningRate
            //this.GenerateMesh ()
            let mutable epoch = 0
            while not cancel.IsCancellationRequested && epoch < numEpochs do
                let history = trainingModel.Fit (dataSource, batchSize = batchSize, epochs = 1.0f, callback = fun h -> callback h)
                trainedPoints <- trainedPoints + history.Batches.Length * batchSize
                epoch <- epoch + 1
        with ex ->
            reportError ex
        if totalTrained > 0 then
            this.Save ()

    member this.Save () =
        match trainingModelO with
        | None -> ()
        | Some trainingModel ->
            let tmpPath = IO.Path.GetTempFileName ()
            trainingModel.SaveArchive (tmpPath)
            if File.Exists (trainingModelPath) then
                File.Delete (trainingModelPath)
            IO.File.Move (tmpPath, trainingModelPath)
            printfn "SAVED MODEL: %s" trainingModelPath

    member this.GenerateVoxels (progress : float32 -> unit) =
        let nx, ny, nz = int project.Settings.Resolution, int project.Settings.Resolution, int project.Settings.Resolution
        let mutable numPoints = 0
        let totalPoints = nx*ny*nz

        let data = data.Value
        let model = getModel ()

        let tpool = System.Buffers.ArrayPool<Tensor>.Shared
        let tapool = System.Buffers.ArrayPool<Tensor[]>.Shared

        let sdf (x : Memory<Vector3>) (y : Memory<Vector4>) =
            let n = x.Length
            let batchTensors = ResizeArray<_> (n)
            let batchOutputs = ResizeArray<_> (n)
            let x = x.Span
            let yspan = y.Span
            for i in 0..(n - 1) do
                //let p = x.[i] - data.VolumeCenter
                let p = data.ClipWorldPoint x.[i]
                if data.IsClipPointOccupied p then
                    let input = Tensor.Array (p.X, p.Y, p.Z, 1.0f)
                    let inputs = tpool.Rent 1
                    inputs.[0] <- input
                    batchTensors.Add inputs
                    batchOutputs.Add i
                else
                    yspan.[i] <- outsideSdf
            let nin = batchTensors.Count
            if nin > 0 then
                let nbatch = batchTensors.Count
                let results = model.Predict (batchTensors.ToArray (), nbatch)
                for i in 0..(nbatch - 1) do
                    tpool.Return batchTensors.[i]
                //tapool.Return batchTensors
                for i in 0..(nin-1) do
                    let r = results.[i].[0]
                    let yvec = Vector4(1.0f, 1.0f, 1.0f, r.[0])
                    //if numPoints < totalPoints / 100 then
                    //    printfn "%g" yvec.W
                    yspan.[batchOutputs.[i]] <- yvec
            numPoints <- numPoints + y.Length
            let p = float32 numPoints / float32 totalPoints
            progress p
            //printfn "NN Sample Progress: %.1f" (p * 100.0)

        let voxels = SdfKit.Voxels.SampleSdf (sdf, data.VolumeMin, data.VolumeMax, nx, ny, nz, batchSize = batchSize, maxDegreeOfParallelism = 2)
        voxels.ClipToBounds ()
        voxels

    member this.GenerateMesh (progress : float32 -> unit) =
        let voxels = this.GenerateVoxels progress
        voxels.ClipToBounds ()
        let mesh = SdfKit.MarchingCubes.CreateMesh (voxels, 0.0f, step = 1)
        //mesh.WriteObj (dataDir + sprintf "/Onewheel_s%d_d%d_c%d_%s_l%d_%d.obj" (int outputScale) (int (1.0f/samplingDistance)) (int (1.0f/lossClipDelta)) (if useTanh then "tanh" else "n") (int (1.0f/learningRate)) trainedPoints)
        mesh

    member this.GenerateMesh (voxels : SdfKit.Voxels) =
        let mesh = SdfKit.MarchingCubes.CreateMesh (voxels, 0.0f, step = 1)
        //mesh.WriteObj (dataDir + sprintf "/Onewheel_s%d_d%d_c%d_%s_l%d_%d.obj" (int outputScale) (int (1.0f/samplingDistance)) (int (1.0f/lossClipDelta)) (if useTanh then "tanh" else "n") (int (1.0f/learningRate)) trainedPoints)
        mesh

    member this.Changed = changed.Publish

    member this.IsTraining = training.IsSome

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


