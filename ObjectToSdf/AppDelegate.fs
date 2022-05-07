namespace ObjectToSdf
open System
open Foundation
open AppKit
open System.IO
open g3

type O2S () =

    let inputDirs = [|"/Volumes/nn/Data/GoogleScannedObjects" |]
    let outputDir = "/Volumes/nn/Data/datasets/sdfs2"

    do if Directory.Exists outputDir |> not then Directory.CreateDirectory outputDir |> ignore

    let files =
        inputDirs
        |> Array.collect (fun d ->
            Directory.GetFiles(d, "*.zip", SearchOption.AllDirectories))

    let numCells = 512
    let cellSize = 2.0 / float numCells
    let numPoints = 250_000

    let processObjBytes (name : string) (stream : Stream) =
        let md5 = Security.Cryptography.MD5.Create()
        let hash = md5.ComputeHash(stream)
        let hashSum = Seq.sum (hash |> Seq.map int)
        let hashString = String.Join ("", hash |> Seq.map (fun x -> x.ToString("x2")))
        let outputPath = Path.Combine (outputDir, (hashString + ".sdf"))
        let meshPath = Path.Combine (outputDir, (hashString + ".obj"))
        if File.Exists outputPath then
            ()
        else
            stream.Position <- 0L

            let random = Random(hashSum)
            let mesh = StandardMeshReader.ReadMesh(stream, "obj")
            let bounds = mesh.GetBounds ()
            MeshTransforms.ConvertZUpToYUp (mesh)
            let ybounds = mesh.GetBounds ()
            MeshTransforms.Translate (mesh, -ybounds.Center)
            let cbounds = mesh.GetBounds ()
            let scale = 0.9 + 0.1 * random.NextDouble ()
            MeshTransforms.Scale (mesh, scale/cbounds.Extents.x, scale/cbounds.Extents.y, scale/cbounds.Extents.z)
            let sbounds = mesh.GetBounds ()
            StandardMeshWriter.WriteMesh(meshPath, mesh, WriteOptions.Defaults) |> ignore
            let sdf = new MeshSignedDistanceGrid(mesh, cellSize)
            sdf.ComputeSigns <- true
            sdf.UseParallel <- true
            sdf.Compute()
            let iso = DenseGridTrilinearImplicit(sdf.Grid, Vector3d(sdf.GridOrigin), cellSize)
            iso.Outside <- 3.0
            if false then
                let c = new MarchingCubes()
                c.Implicit <- iso
                c.Bounds <- mesh.CachedBounds
                c.CubeSize <- c.Bounds.MaxDim / 128.0
                c.Bounds.Expand(3.0 * c.CubeSize)                
                c.Generate()
                StandardMeshWriter.WriteMesh(meshPath, c.Mesh, WriteOptions.Defaults) |> ignore
            let tempPath =
                let p = Path.GetTempFileName ()
                use s = new FileStream(p, FileMode.Create, FileAccess.Write)
                use w = new BinaryWriter (s)
                for i in 0..(numPoints - 1) do
                    let wantSurface = random.Next(100) < 75
                    let mutable p = Vector3d (random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0)
                    let mutable d = iso.Value (&p) |> float32
                    while Single.IsInfinity d || Single.IsNaN d || (wantSurface && abs d > 2.0f) do
                        p <- Vector3d (random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0)
                        d <- iso.Value (&p) |> float32
                    w.Write(float32 p.x)
                    w.Write(float32 p.y)
                    w.Write(float32 p.z)
                    w.Write(float32 d)
                p
            File.Move (tempPath, outputPath)

    let mutable numFilesProc = 0

    let processFile (zipPath : string) =
        //printfn "ZIP: %s" zipPath
        let name = (Path.GetFileNameWithoutExtension(zipPath))
        use zs = File.OpenRead zipPath
        use z = new Compression.ZipArchive (zs)
        z.Entries
        |> Seq.filter (fun x -> x.FullName.EndsWith(".obj", StringComparison.InvariantCultureIgnoreCase))
        |> Seq.iteri (fun i x ->
            use s = x.Open()
            use d = new MemoryStream(int x.Length)
            s.CopyTo d
            d.Position <- 0L
            processObjBytes (sprintf "%s_%d" name i) d
            let progress = 100.0 * (float numFilesProc / float files.Length)
            printfn "[%.1f] %s/%s (%d)" progress name x.FullName d.Length
            ())
        let _ = Threading.Interlocked.Increment(&numFilesProc)
        ()

    member this.Files = files

    member this.Run () =
        printfn "%d FILES" this.Files.Length
        let options = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = 3)
        Threading.Tasks.Parallel.ForEach (files, options, processFile) |> ignore
        ()



[<Register ("AppDelegate")>]
type AppDelegate () =
    inherit NSApplicationDelegate ()

    override this.DidFinishLaunching (n) =
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            let o2s = O2S()
            o2s.Run()) |> ignore
        ()
