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

    let processObjBytes (name : string) (stream : Stream) =
        let md5 = Security.Cryptography.MD5.Create()
        let hash = Convert.ToBase64String(md5.ComputeHash(stream))
        stream.Position <- 0L
        let mesh = StandardMeshReader.ReadMesh(stream, "obj")
        ()

    let mutable numFilesProc = 0

    let processFile (zipPath : string) =
        //printfn "ZIP: %s" zipPath
        let n = Threading.Interlocked.Increment(&numFilesProc)
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
            processObjBytes x.FullName d
            printfn "[%.1f] %s/%s (%d)" progress (Path.GetFileName(zipPath)) x.FullName d.Length
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
