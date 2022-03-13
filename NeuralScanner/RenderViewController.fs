namespace NeuralScanner

open System
open Foundation
open CoreGraphics
open ObjCRuntime
open UIKit
open ARKit
open SdfKit
open System.Numerics
open CoreML

#nowarn "9"
open FSharp.NativeInterop

type RenderViewController () =
    inherit UIViewController ()

    let imageView = new UIImageView()

    let mutable sdfModel : MLModel option = None

    let vec3ToUIImage (data : Vec3Data) : UIImage =

        let bytesPerRow = data.Width * 4
        let bytes : byte[] = Array.zeroCreate (data.Height * bytesPerRow)
        let fvalues = data.FloatMemory.Span
        let mutable bi = 0
        let mutable fi = 0
        let n = fvalues.Length
        while fi < n do
            let fr = fvalues.[fi]
            let fg = fvalues.[fi+1]
            let fb = fvalues.[fi+2]
            let br = byte (fr * 255.0f)
            let bg = byte (fg * 255.0f)
            let bb = byte (fb * 255.0f)
            let ba = 255uy
            bytes.[bi] <- br
            bytes.[bi+1] <- bg
            bytes.[bi+2] <- bb
            bytes.[bi+3] <- ba
            fi <- fi + 3
            bi <- bi + 4

        use colorSpace = CGColorSpace.CreateGenericRgb()
        use context = new CGBitmapContext(bytes, nint data.Width, nint data.Height, nint 8, nint bytesPerRow, colorSpace, CGBitmapFlags.NoneSkipLast)
        let cgimage = context.ToImage()
        UIImage.FromImage(cgimage)

    override this.ViewDidLoad() =
        base.ViewDidLoad()
        this.View.BackgroundColor <- UIColor.SystemBackgroundColor
        imageView.Frame <- this.View.Bounds
        imageView.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions
        imageView.ContentMode <- UIViewContentMode.ScaleAspectFit
        this.View.AddSubview(imageView)
        async {
            let mutable compileError : NSError = null
            let modelPath = NSBundle.MainBundle.GetUrlForResource("SDF", "mlmodel")
            printfn "Loading model %O" modelPath
            let compiledModelUrl = MLModel.CompileModel(modelPath, &compileError)
            if compileError = null then
                MLModel.LoadContents(compiledModelUrl, new MLModelConfiguration(), fun model loadEror ->
                    printfn "MODEL LOADED: %O" model
                    sdfModel <- model |> Option.ofObj
                    this.RenderScene()
                    ())
            else
                printfn "Failed to load model"
        }
        |> Async.Start


    member private this.RenderScene () =
        match sdfModel with
        | None -> ()
        | Some sdfModel ->
            let width = 160
            let height = 120
            let numParallel = 20
            let depthIterations = 4
            let numTotalPoints = (width*height) * (depthIterations + 8)
            let mutable numProcPoints = 0
            let maxBatchSize = width * height / numParallel
            let pointProviders = Array.init maxBatchSize (fun _ -> new PointProvider())
            let iProviders =
                pointProviders
                |> Array.map(fun x -> x :> IMLFeatureProvider)
            let inputBatches = new CoreML.MLArrayBatchProvider(iProviders)
            let semaphore = new Threading.SemaphoreSlim(2, 2)

            let sdf = Sdf(fun points colorAndDistances ->
                printfn "SDF Request"
                semaphore.Wait()
                printfn "SDF Run"
                let n = points.Length
                let sw = new Diagnostics.Stopwatch()
                try
                    sw.Start()
                
                    let mutable error : NSError = null
                    let pointsSpan = points.Span
                    let distsSpan = colorAndDistances.Span
                    for i in 0..(n-1) do
                        let point = pointsSpan.[i]
                        pointProviders.[i].SetPoint point
                    use outputBatches = sdfModel.GetPredictions(inputBatches, &error)
                    if outputBatches.Count < nint n then
                        failwithf "Network didn't provide enough output values"
                    for i in 0..(n-1) do
                        let fs = outputBatches.GetFeatures(nint i)
                        let names = fs.FeatureNames
                        let name = names.AnyObject
                        let value = fs.GetFeatureValue(string name)
                        match value.MultiArrayValue with
                        | null -> failwith "Output from NN is null!"
                        | marray ->
                            let p = NativePtr.ofNativeInt<float32> marray.DataPointer
                            let distance = NativePtr.get p 0
                            let r = (NativePtr.get p 1)*0.5f + 0.5f
                            let g = (NativePtr.get p 2)*0.5f + 0.5f
                            let b = (NativePtr.get p 3)*0.5f + 0.5f
                            let colorAndDist = Vector4(r, g, b, distance)
                            distsSpan.[i] <- colorAndDist
                    sw.Stop()
                with ex ->
                    printfn "ERROR %O" ex
                let duration = sw.Elapsed.TotalSeconds
                numProcPoints <- numProcPoints + n
                let percentComplete = 100.0 * float numProcPoints / float numTotalPoints
                printfn "SDF %g%% Complete %d points in %g seconds (%g points/sec)" percentComplete n duration (float n / duration)
                semaphore.Release() |> ignore
                ())

            let sdf = SdfFuncs.Sphere(0.75f).ToSdf()

            let viewTransform =
                Matrix4x4.CreateLookAt(Vector3(2.0f, 1.0f, 2.0f),
                                       Vector3.Zero,
                                       Vector3.UnitY)

            let imageVecs = sdf.ToImage(width, height,
                                        viewTransform,
                                        batchSize = maxBatchSize,
                                        depthIterations = depthIterations,
                                        nearPlaneDistance = 0.1f,
                                        maxDegreeOfParallelism = 1)
            let image = vec3ToUIImage imageVecs
            this.BeginInvokeOnMainThread (fun _ ->
                imageView.Image <- image)

and PointProvider () =
    inherit NSObject ()

    let featureNames = new NSSet<NSString>(new NSString("xyz"))
    let mutable marrayError : NSError = null
    let marray = new CoreML.MLMultiArray([|nint 1; nint 3|], MLMultiArrayDataType.Float32, &marrayError)
    let featureValue = CoreML.MLFeatureValue.Create(marray)

    member this.SetPoint (point : Vector3) =
        let p = NativePtr.ofNativeInt<float32> marray.DataPointer
        NativePtr.set p 0 point.X
        NativePtr.set p 1 point.Y
        NativePtr.set p 2 point.Z

    interface IMLFeatureProvider with
        member this.FeatureNames = featureNames
        member this.GetFeatureValue (name) = featureValue
