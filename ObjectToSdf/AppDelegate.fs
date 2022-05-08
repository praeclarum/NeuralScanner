namespace ObjectToSdf
open System
open Foundation
open AppKit
open System.IO
open g3
open NeuralScanner

type O2S () =

    let objectInfo = ObjectInfo.Load()

    let outputBaseDir = "/Volumes/nn/Data/datasets/sdfs6"

    do if Directory.Exists outputBaseDir |> not then Directory.CreateDirectory outputBaseDir |> ignore

    let numCells = 512
    let cellSize = 2.0 / float numCells
    let numPoints = 1_000_000

    let processObjBytes (stream : Stream) outputDir =
        let md5 = Security.Cryptography.MD5.Create()
        let hash = md5.ComputeHash(stream)
        let hashSum = Seq.sum (hash |> Seq.map int)
        let hashString = String.Join ("", hash |> Seq.map (fun x -> x.ToString("x2")))
        let meshPath = Path.Combine (outputDir, sprintf "%s.obj" hashString)
        let outputPath = Path.Combine (outputDir, sprintf "%s.sdf" hashString)
        let voxelsPath = Path.Combine (outputDir, sprintf "%s_Voxels.obj" hashString)

        if true || (File.Exists meshPath && File.Exists outputPath) |> not then

            let random = Random(hashSum)
            stream.Position <- 0L
            let mesh = StandardMeshReader.ReadMesh(stream, "obj")
            let ybounds = mesh.GetBounds ()
            MeshTransforms.Translate (mesh, -ybounds.Center)
            let cbounds = mesh.GetBounds ()
            let maxExtent = 0.97
            let scale = maxExtent / (cbounds.MaxDim / 2.0)
            MeshTransforms.Scale (mesh, scale, scale, scale)
            let sbounds = mesh.GetBounds ()
            StandardMeshWriter.WriteMesh(meshPath, mesh, WriteOptions.Defaults) |> ignore

            let sdf = new MeshSignedDistanceGrid(mesh, cellSize)
            sdf.ComputeSigns <- true
            sdf.UseParallel <- true
            sdf.Compute()
            let iso = DenseGridTrilinearImplicit(sdf.Grid, Vector3d(sdf.GridOrigin), float sdf.CellSize)
            iso.Outside <- 2.0
            if false then
                let c = new MarchingCubes()
                c.Implicit <- iso
                c.Bounds <- mesh.CachedBounds
                c.CubeSize <- c.Bounds.MaxDim / 128.0
                c.Bounds.Expand(3.0 * c.CubeSize)                
                c.Generate()
                StandardMeshWriter.WriteMesh(voxelsPath, c.Mesh, WriteOptions.Defaults) |> ignore
            let tempPath =
                let p = Path.GetTempFileName ()
                use s = new FileStream(p, FileMode.Create, FileAccess.Write)
                use w = new BinaryWriter (s)
                for i in 0..(numPoints - 1) do
                    let wantSurface = random.Next(100) < 75
                    let mutable p = Vector3d (random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0)
                    p <- 0.99*Vector3d.One
                    let mutable d = iso.Value (&p) |> float32
                    while Single.IsInfinity d || Single.IsNaN d || (wantSurface && abs d > 1.0f) do
                        p <- Vector3d (random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0)
                        d <- iso.Value (&p) |> float32
                    w.Write(float32 p.x)
                    w.Write(float32 p.y)
                    w.Write(float32 p.z)
                    w.Write(float32 d)
                p

            if File.Exists outputPath then
                File.Delete outputPath
            File.Move (tempPath, outputPath)

    let mutable numFilesProc = 0

    let processFile (outDir : string) (filePath : string) =
        let ext = (Path.GetExtension filePath).ToLowerInvariant ()
        if ext = ".obj" then
            use d = File.OpenRead filePath
            processObjBytes d outDir
            let progress = 0.0//100.0 * (float numFilesProc / float files.Length)
            printfn "[%.1f] %s (%d)" progress filePath d.Length
        else
            //printfn "ZIP: %s" zipPath
            let name = (Path.GetFileNameWithoutExtension(filePath))
            use zs = File.OpenRead filePath
            use z = new Compression.ZipArchive (zs)
            z.Entries
            |> Seq.filter (fun x -> x.FullName.EndsWith(".obj", StringComparison.InvariantCultureIgnoreCase))
            |> Seq.iteri (fun i x ->
                use s = x.Open()
                use d = new MemoryStream(int x.Length)
                s.CopyTo d
                d.Position <- 0L
                processObjBytes d outDir
                let progress = 0.0//100.0 * (float numFilesProc / float files.Length)
                printfn "[%.1f] %s/%s (%d)" progress name x.FullName d.Length
                ())
        let _ = Threading.Interlocked.Increment(&numFilesProc)
        ()

    member this.Run () =
        let trainingDataDir = objectInfo.TrainingDataDirectory
        for cat in objectInfo.Categories |> Seq.filter (fun x -> true || x.CategoryId = "Spaceships") do
            let inDir = Path.Combine(trainingDataDir, cat.CategoryId)
            let outDir = Path.Combine(outputBaseDir, cat.CategoryId)
            Directory.CreateDirectory (outDir) |> ignore
            let files = Directory.GetFiles(inDir, "*.obj", SearchOption.AllDirectories)
            printfn "%d FILES" files.Length
            let options = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = 3)
            Threading.Tasks.Parallel.ForEach (files, options, processFile outDir) |> ignore
            ()



[<Register ("AppDelegate")>]
type AppDelegate () =
    inherit NSApplicationDelegate ()

    override this.DidFinishLaunching (n) =
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            let o2s = O2S()
            o2s.Run()) |> ignore
        ()
