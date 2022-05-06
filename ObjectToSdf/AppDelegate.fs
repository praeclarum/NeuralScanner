namespace ObjectToSdf
open System
open Foundation
open AppKit
open System.IO
open g3

type O2S () =

    let inputDirs = [|"/Volumes/nn/Data/GoogleScannedObjects" |]
    let files =
        inputDirs
        |> Array.collect (fun d ->
            Directory.GetFiles(d, "*.zip", SearchOption.AllDirectories))

    let numCells = 128
    let cellSize = 2.0 / float numCells
    let numPoints = 10

    let processObjBytes (name : string) (stream : Stream) =
        let md5 = Security.Cryptography.MD5.Create()
        let hash = md5.ComputeHash(stream)
        let hashSum = Seq.sum (hash |> Seq.map int)
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
        let sdf = new MeshSignedDistanceGrid(mesh, cellSize)
        sdf.Compute()
        let iso = DenseGridTrilinearImplicit(sdf.Grid, Vector3d(sdf.GridOrigin), cellSize)
        for i in 0..(numPoints - 1) do
            let mutable p = Vector3d (random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0, random.NextDouble () * 2.0 - 1.0)
            let d = iso.Value (&p)
            ()
        ()

    let mutable numFilesProc = 0

    let processFile (zipPath : string) =
        //printfn "ZIP: %s" zipPath
        let n = Threading.Interlocked.Increment(&numFilesProc)
        let name = (Path.GetFileName(zipPath))
        use zs = File.OpenRead zipPath
        use z = new Compression.ZipArchive (zs)
        z.Entries
        |> Seq.filter (fun x -> x.FullName.EndsWith(".obj", StringComparison.InvariantCultureIgnoreCase))
        |> Seq.iteri (fun i x ->
            use s = x.Open()
            use d = new MemoryStream(int x.Length)
            s.CopyTo d
            let progress = 100.0 * (float numFilesProc / float files.Length)
            d.Position <- 0L
            processObjBytes name d
            printfn "[%.1f] %s/%s (%d)" progress name x.FullName d.Length
            ())
        ()

    member this.Files = files

    member this.Run () =
        printfn "%d FILES" this.Files.Length
        Threading.Tasks.Parallel.ForEach (files, processFile) |> ignore
        ()



[<Register ("AppDelegate")>]
type AppDelegate () =
    inherit NSApplicationDelegate ()

    override this.DidFinishLaunching (n) =
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            let o2s = O2S()
            o2s.Run()) |> ignore
        ()
