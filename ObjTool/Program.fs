open System
open System.IO

open g3

let args = System.Environment.GetCommandLineArgs()

type Command =
    | YUp
    | Y90
    | Y180
    | Y270
    | Voxelize

let inputFiles = ResizeArray<_> ()
let commands = ResizeArray<_> ()

for i in 0..(args.Length-1) do
    let ext = Path.GetExtension(args[i]).ToLowerInvariant()
    if ext = ".obj" || ext = ".zip" || ext = ".off" then
        inputFiles.Add(args[i])
    else
        match args[i] with
        | "yup" -> commands.Add(YUp)
        | "y90" -> commands.Add(Y90)
        | "y180" -> commands.Add(Y180)
        | "y270" -> commands.Add(Y270)
        | "vox" -> commands.Add(Voxelize)
        | _ -> ()

let processObj (sourcePath: string, inputStream : Stream) =
    printfn "Processing: %s" sourcePath
    let mutable mesh = StandardMeshReader.ReadMesh(inputStream, "obj")
    let path = Path.ChangeExtension(sourcePath, ".obj")
    for c in commands do
        match c with
        | YUp ->
            printfn "YUp"
            MeshTransforms.ConvertZUpToYUp (mesh)
        | Y90 ->
            printfn "Y90"
            let center = mesh.CachedBounds.Center
            MeshTransforms.Rotate(mesh, center, Quaterniond.AxisAngleD(Vector3d.AxisY, 90.0))
        | Y180 ->
            printfn "Y180"
            let center = mesh.CachedBounds.Center
            MeshTransforms.Rotate(mesh, center, Quaterniond.AxisAngleD(Vector3d.AxisY, 180.0))
        | Y270 ->
            printfn "Y270"
            let center = mesh.CachedBounds.Center
            MeshTransforms.Rotate(mesh, center, Quaterniond.AxisAngleD(Vector3d.AxisY, 270.0))
        | Voxelize ->
            printfn "Voxelize"
            let numCells = 512
            let cellSize = mesh.CachedBounds.MaxDim / float numCells
            let sdf = MeshSignedDistanceGrid(mesh, cellSize)
            sdf.Compute()
            let iso = DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOrigin, float sdf.CellSize)
            let c = MarchingCubes ()
            c.Implicit <- iso
            c.Bounds <- mesh.CachedBounds
            c.CubeSize <- c.Bounds.MaxDim / float numCells
            c.Bounds.Expand(3.0 * float c.CubeSize)
            c.Generate()
            mesh <- c.Mesh
            
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

let offToObj (offPath : string) : string =
    let objPath = Path.ChangeExtension(offPath, ".obj")
    if File.Exists(objPath) then
        File.Delete(objPath)
    use writer = new StreamWriter(objPath)
    use reader = new StreamReader(offPath)
    use vstream = new MemoryStream()
    let vbw = new BinaryWriter (vstream)
    use istream = new MemoryStream()
    let ibw = new BinaryWriter (istream)
    let mutable line = reader.ReadLine()
    let mutable hasHeader = false
    let mutable numVerts = 0
    let mutable remVerts = 0
    let splits = [| ' '; '\t' |]
    while line <> null do
        let parts = line.Split(splits, StringSplitOptions.RemoveEmptyEntries)
        if parts.Length >= 3 then
            if not hasHeader then
                numVerts <- Int32.Parse parts.[0]
                remVerts <- numVerts
                hasHeader <- true
            elif remVerts > 0 then
                writer.WriteLine("v " + parts.[0] + " " + parts.[1] + " " + parts.[2])
                remVerts <- remVerts - 1
            elif parts.Length >= 4 then
                let iparts = parts |> Array.map Int32.Parse
                let nindices = iparts.[0]
                for i in 0..(nindices - 3) do
                    writer.WriteLine(sprintf "f %d %d %d" (iparts.[1]+1) (iparts.[i+2]+1) (iparts.[i+3]+1))
        line <- reader.ReadLine()
    objPath

printfn "OBJ Tool by Frank Krueger"
for f in inputFiles do
    let ext = Path.GetExtension(f).ToLowerInvariant()
    match ext with
    | ".obj" ->
        use s = File.OpenRead(f)
        processObj(Path.GetFullPath(f), s)
    | ".off" ->
        let offPath = Path.GetFullPath(f)
        let objPath = offToObj offPath
        use s = File.OpenRead(objPath)
        processObj(objPath, s)
    | ".zip" ->
        use z = Compression.ZipFile.OpenRead(f)
        match z.Entries |> Seq.tryFind(fun e -> e.Name.EndsWith(".obj", System.StringComparison.InvariantCultureIgnoreCase)) with
        | Some(e) ->
            use s = e.Open()
            processObj(Path.GetFullPath(f), s)
        | None -> ()
    | _ -> ()

