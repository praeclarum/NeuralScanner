open g3
open System.IO

let args = System.Environment.GetCommandLineArgs()

type Command =
    | YUp
    | Y90
    | Y180
    | Y270

let inputFiles = ResizeArray<_> ()
let commands = ResizeArray<_> ()

for i in 0..(args.Length-1) do
    let ext = Path.GetExtension(args[i]).ToLowerInvariant()
    if ext = ".obj" || ext = ".zip" then
        inputFiles.Add(args[i])
    else
        match args[i] with
        | "yup" -> commands.Add(YUp)
        | "y90" -> commands.Add(Y90)
        | "y180" -> commands.Add(Y180)
        | _ -> ()

let processObj (sourcePath: string, inputStream : Stream) =
    printfn "Processing: %s" sourcePath
    let mesh = StandardMeshReader.ReadMesh(inputStream, "obj")
    let path = Path.ChangeExtension(sourcePath, ".obj")
    for c in commands do
        match c with
        | YUp ->
            printfn "YUp"
            MeshTransforms.ConvertZUpToYUp (mesh)
        | Y90 ->
            printfn "Y90"
            let center = mesh.GetBounds().Center
            MeshTransforms.Rotate(mesh, center, Quaterniond.AxisAngleD(Vector3d.AxisY, 90.0))
        | Y180 ->
            printfn "Y180"
            let center = mesh.GetBounds().Center
            MeshTransforms.Rotate(mesh, center, Quaterniond.AxisAngleD(Vector3d.AxisY, 180.0))
        | Y270 ->
            printfn "Y270"
            let center = mesh.GetBounds().Center
            MeshTransforms.Rotate(mesh, center, Quaterniond.AxisAngleD(Vector3d.AxisY, 270.0))
    if File.Exists(path+".bak") then
        File.Delete(path+".bak")
    let mutable bakSource = false
    if File.Exists(path) then
        bakSource <- true
        File.Move(path, path + ".bak")
    let mutable options = WriteOptions.Defaults
    options.bPerVertexNormals <- false
    options.bCombineMeshes <- false
    let result = StandardMeshWriter.WriteMesh(path, mesh, options)
    if result.code = IOCode.Ok then
        printfn "Wrote: %s" path
        if bakSource then
            File.Delete(path + ".bak")
    else
        printfn "Failed: %A" result.message
        if bakSource then
            if File.Exists(path) then
                File.Delete(path)
            File.Move(path + ".bak", path)

printfn "OBJ Tool by Frank Krueger"
for f in inputFiles do
    let ext = Path.GetExtension(f).ToLowerInvariant()
    match ext with
    | ".obj" ->
        use s = File.OpenRead(f)
        processObj(Path.GetFullPath(f), s)
    | ".zip" ->
        use z = Compression.ZipFile.OpenRead(f)
        match z.Entries |> Seq.tryFind(fun e -> e.Name.EndsWith(".obj", System.StringComparison.InvariantCultureIgnoreCase)) with
        | Some(e) ->
            use s = e.Open()
            processObj(Path.GetFullPath(f), s)
        | None -> ()
    | _ -> ()

