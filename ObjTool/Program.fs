open g3
open System.IO

let args = System.Environment.GetCommandLineArgs()

type Command =
    | YUp
    | Y90
    | Y180
    | Y270

let objFiles = ResizeArray<_> ()
let commands = ResizeArray<_> ()

for i in 0..(args.Length-1) do
    if args[i].EndsWith(".obj") then
        objFiles.Add(args[i])
    else
        match args[i] with
        | "yup" -> commands.Add(YUp)
        | "y90" -> commands.Add(Y90)
        | "y180" -> commands.Add(Y180)
        | _ -> ()

let processObj (path: string) =
    printfn "Processing: %s" path
    let mesh = StandardMeshReader.ReadMesh(path)
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
    File.Move(path, path + ".bak")
    let mutable options = WriteOptions.Defaults
    options.bPerVertexNormals <- false
    options.bCombineMeshes <- false
    let result = StandardMeshWriter.WriteMesh(path, mesh, options)
    if result.code = IOCode.Ok then
        printfn "Success"
        File.Delete(path + ".bak")
    else
        printfn "Failed: %A" result.message
        if File.Exists(path) then
            File.Delete(path)
        File.Move(path + ".bak", path)


printfn "OBJ Tool by Frank Krueger"
for f in objFiles do
    processObj(Path.GetFullPath(f))

