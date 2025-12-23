// Step 1: 3D Surface Reconstruction

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using itk.simple;
using g3;

public class Create3DModelWindow : EditorWindow
{
    // Window fields
    string datasetFolder = "";
    string modelName = "MyModel";
    bool groupEnabled = false;
    double numCells = 1.0;
    float edgeLengthMultiplier = 1.0f;
    int remeshPasses = 0;
    int reducedTriangleCount = 0;
    // store last reconstruction result so Export can write it directly
    DMesh3 _result = null;

    // Open the window via menu
    [MenuItem("CS116A/Create3DModelWindow")]
    public static void OpenWindow()
    {
        // Use GetWindow to open or focus the editor window
        EditorWindow.GetWindow(typeof(Create3DModelWindow), false, "Create 3D Model");
    }

    private void OnGUI()
    {
        GUILayout.Label("Settings", EditorStyles.boldLabel);

        if (GUILayout.Button("Browse a Folder"))
        {
            // Open a folder panel to select a folder
            datasetFolder = EditorUtility.OpenFolderPanel("Select DICOM Directory", "", "");
            if (!string.IsNullOrEmpty(datasetFolder))
                modelName = Path.GetFileName(datasetFolder);
        }

        datasetFolder = EditorGUILayout.TextField("Selected Folder:", datasetFolder);
        modelName = EditorGUILayout.TextField("Model Name:", modelName);

        groupEnabled = EditorGUILayout.BeginToggleGroup("Optional Settings", groupEnabled);
        numCells = EditorGUILayout.DoubleField("NumCells:", numCells);
        edgeLengthMultiplier = EditorGUILayout.FloatField("EdgeLengthMultiplier:", edgeLengthMultiplier);
        remeshPasses = EditorGUILayout.IntField("Smoothing Iteration:", remeshPasses);
        reducedTriangleCount = EditorGUILayout.IntField("ReducedTriangleCount:", reducedTriangleCount);
        EditorGUILayout.EndToggleGroup();

        GUILayout.Space(8);

        if (GUILayout.Button("Reconstruct 3D Model"))
        {
            if (!string.IsNullOrEmpty(datasetFolder))
            {
                // Run DICOM series -> marching cubes reconstruction
                try {
                    EditorUtility.DisplayProgressBar("Reconstructing", "Loading DICOM series...", 0.05f);

                    VectorString dicomNames = ImageSeriesReader.GetGDCMSeriesFileNames(datasetFolder);
                    if (dicomNames == null || dicomNames.Count == 0) {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("Load Failed", "No DICOM series found in selected folder.", "OK");
                        return;
                    }

                    var seriesReader = new ImageSeriesReader();
                    seriesReader.SetFileNames(dicomNames);
                    var rawImage = seriesReader.Execute();

                    // Resample to isotropic spacing for stable marching cubes
                    EditorUtility.DisplayProgressBar("Reconstructing", "Resampling volume to 1x1x1 spacing...", 0.15f);
                    var resampledImage = ResampleVolumeImage(rawImage);

                    var volumeImage = SimpleITK.Cast(resampledImage, PixelIDValueEnum.sitkFloat32);
                    int nx = (int)volumeImage.GetWidth();
                    int ny = (int)volumeImage.GetHeight();
                    int nz = (int)volumeImage.GetDepth();
                    int numPixels = nx * ny * nz;

                    EditorUtility.DisplayProgressBar("Reconstructing", "Copying image buffer...", 0.25f);
                    IntPtr bufferImg = volumeImage.GetBufferAsFloat();
                    float[] voxels = new float[numPixels];
                    Marshal.Copy(bufferImg, voxels, 0, numPixels);

                    EditorUtility.DisplayProgressBar("Reconstructing", "Filling grid...", 0.35f);
                    DenseGrid3f grid = new DenseGrid3f(nx, ny, nz, 1);
                    for (int k = 0; k < nz; k++)
                    {
                        for (int j = 0; j < ny; j++)
                        {
                            for (int i = 0; i < nx; i++)
                            {
                                int idx = i + nx * (j + ny * k);
                                float pixel = voxels[idx];
                                // treat positive voxels as interior; marching cubes expects negative for interior
                                grid[idx] = (pixel > 0f) ? -pixel : 0f;
                            }
                        }
                    }

                    EditorUtility.DisplayProgressBar("Reconstructing", "Running Marching Cubes...", 0.55f);
                    double cellsize = volumeImage.GetSpacing()[0];
                    double useNumCells = Math.Max(32, (int)numCells);
                    var iso = new DenseGridTrilinearImplicit(grid, Vector3f.Zero, cellsize);

                    MarchingCubes mc = new MarchingCubes();
                    mc.Implicit = iso;
                    mc.RootMode = MarchingCubes.RootfindingModes.Bisection;
                    mc.RootModeSteps = 5;
                    mc.Bounds = iso.Bounds();
                    mc.CubeSize = mc.Bounds.MaxDim / useNumCells;
                    mc.Bounds.Expand(3 * mc.CubeSize);
                    mc.Generate();

                    var meshResult = mc.Mesh;

                    // Center mesh around centroid of volume
                    EditorUtility.DisplayProgressBar("Reconstructing", "Centering mesh...", 0.70f);
                    MeshNormals.QuickCompute(meshResult);
                    var centroid = new Vector3d(nx, ny, nz) / 2.0;
                    MeshTransforms.Translate(meshResult, centroid * -1);

                    // Remeshing / smoothing
                    if (remeshPasses > 0)
                    {
                        EditorUtility.DisplayProgressBar("Reconstructing", "Smoothing / remeshing...", 0.78f);
                        Remesher remesh = new Remesher(meshResult);
                        remesh.PreventNormalFlips = true;
                        remesh.SetTargetEdgeLength((float)edgeLengthMultiplier);
                        remesh.SmoothSpeedT = 1.0f;
                        remesh.SetProjectionTarget(MeshProjectionTarget.Auto(meshResult));
                        for (int p = 0; p < remeshPasses; ++p)
                            remesh.BasicRemeshPass();
                    }

                    // Decimation
                    if (reducedTriangleCount > 0)
                    {
                        EditorUtility.DisplayProgressBar("Reconstructing", "Decimating mesh...", 0.88f);
                        Reducer reducer = new Reducer(meshResult);
                        int target = Math.Min((int)meshResult.TriangleCount, reducedTriangleCount);
                        reducer.ReduceToTriangleCount(target);
                    }

                    // Finalize: flip coords and recompute normals
                    MeshTransforms.FlipLeftRightCoordSystems(meshResult);
                    MeshNormals.QuickCompute(meshResult);

                    // store result for later export and save to disk
                    _result = meshResult;
                    var savePath = $"{Application.dataPath}/{modelName}.obj";
                    Debug.Log(savePath);
                    g3UnityUtils.WriteOutputMesh(_result, savePath);

                    // create a scene object to visualize the mesh
                    GameObject resultGO = new GameObject(modelName);
                    resultGO.transform.position = Vector3.zero;
                    resultGO.AddComponent<MeshFilter>();
                    resultGO.AddComponent<MeshRenderer>().material = new Material(Shader.Find("Standard"));
                    g3UnityUtils.SetGOMesh(resultGO, meshResult);

                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Reconstruction Complete", $"Created and saved model '{modelName}' with {meshResult.TriangleCount} triangles.\nSaved to {savePath}", "OK");
                }
                catch (Exception ex)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogError("Reconstruction error: " + ex);
                    EditorUtility.DisplayDialog("Error", ex.Message, "OK");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Missing Folder", "Please select a DICOM folder first.", "OK");
            }
        }

        GUILayout.Space(6);

        if (GUILayout.Button("Export Selected Mesh as OBJ"))
        {
            // Export mesh from currently selected GameObject in the scene
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a GameObject with a MeshFilter in the scene.", "OK");
            }
            else
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                {
                    EditorUtility.DisplayDialog("No Mesh", "Selected GameObject has no mesh to export.", "OK");
                }
                else
                {
                    // Convert Unity mesh to g3 DMesh3
                    DMesh3 d = g3UnityUtils.UnityMeshToDMesh(mf.sharedMesh);

                    // Build save path and write OBJ using g3UnityUtils
                    var savePath = $"{Application.dataPath}/{modelName}.obj";
                    Debug.Log(savePath);
                    g3UnityUtils.WriteOutputMesh(d, savePath);

                    EditorUtility.DisplayDialog("Export Complete", $"Saved OBJ to:\n{savePath}", "OK");
                }
            }
        }
    }

    private Image ResampleVolumeImage(Image _volumeImage)
    {
        // Create a ResampleImageFilter
        ResampleImageFilter resample = new ResampleImageFilter();

        // Set the interpolator (e.g., Linear)
        resample.SetInterpolator(InterpolatorEnum.sitkLinear);

        // Set the output direction and origin to match the input image
        resample.SetOutputDirection(_volumeImage.GetDirection());
        resample.SetOutputOrigin(_volumeImage.GetOrigin());

        // Define the new spacing
        VectorDouble newSpacing = new VectorDouble { 1.0, 1.0, 1.0 }; //To get it works with Marching cube's cell
        resample.SetOutputSpacing(newSpacing);

        // Calculate the new size based on the new spacing
        var originalSize = _volumeImage.GetSize();
        var originalSpacing = _volumeImage.GetSpacing();
        VectorUInt32 newSize = new VectorUInt32();

        for (int i = 0; i < _volumeImage.GetDimension(); i++)
        {
            newSize.Add((uint)Math.Ceiling(originalSize[i] * originalSpacing[i] / newSpacing[i]));
        }

        resample.SetSize(newSize);

        // Set a default pixel value for points outside the original image bounds
        resample.SetDefaultPixelValue(0.0);

        // Execute the resampling
        var volumeImage = resample.Execute(_volumeImage);

        return volumeImage;
    }

}

