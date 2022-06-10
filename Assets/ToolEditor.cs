using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Linq;

public class ToolEditor : EditorWindow
{

    [MenuItem("Tools/Tool")]
    public static void OpenGrimm() => GetWindow<ToolEditor>();

    public float radius = 2f;
    public int spawnCount = 8;
    public GameObject spawnPrefab = null;
    public Material previewMaterial;

    SerializedObject so;
    SerializedProperty propRadius;
    SerializedProperty propSpawnCount;
    SerializedProperty propSpawnPrefab;
    SerializedProperty propPreviewMaterial;

    public struct RandomData
    {
        public Vector2 pointInDisc;
        public float randAngleDeg;
        public void SetRandomValues()
        {
            pointInDisc = Random.insideUnitCircle;
            randAngleDeg = Random.value * 360;
        }
    }

    RandomData[] randPoints;
    GameObject[] prefabs;



    private void OnEnable()
    {
        so = new SerializedObject(this);
        propRadius = so.FindProperty("radius");
        propSpawnCount = so.FindProperty("spawnCount");
        propSpawnPrefab = so.FindProperty("spawnPrefab");
        propPreviewMaterial = so.FindProperty("previewMaterial");
        GenerateRandomPoints();
        SceneView.duringSceneGui += DuringSceneGUI;

        //load prefabs

        string[] guids = AssetDatabase.FindAssets("t:prefab", new[] {"Assets/Prefabs"});
        IEnumerable<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath);
        prefabs = paths.Select( AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
    }

    private void OnDisable() => SceneView.duringSceneGui -= DuringSceneGUI;


    void GenerateRandomPoints()
    {
        randPoints = new RandomData[spawnCount];
        for(int i = 0; i < spawnCount; i++)
        {
            randPoints[i].SetRandomValues();
        }
    }


    private void OnGUI()
    {
        so.Update();
        EditorGUILayout.PropertyField(propRadius);
        propRadius.floatValue = Mathf.Max(1f, propRadius.floatValue); // = AtLeast Function (2h54 part 2/4)
        EditorGUILayout.PropertyField(propSpawnCount);
        propSpawnCount.intValue = Mathf.Max(1, propSpawnCount.intValue);
        EditorGUILayout.PropertyField(propSpawnPrefab);
        EditorGUILayout.PropertyField(propPreviewMaterial);

        
        if (so.ApplyModifiedProperties() )
        {
            GenerateRandomPoints();
            SceneView.RepaintAll();
        }

        //if clicked left mouse button in the editor window
        if(Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            GUI.FocusControl(null);
            Repaint();
        }
    } 

    void DrawSphere(Vector3 pos)
    {
        Handles.SphereHandleCap(-1, pos, Quaternion.identity, 0.1f, EventType.Repaint);
    }

    void TrySpawnObjects(List<Pose> poses)
    {
        if(spawnPrefab == null)
        {
            return;
        }

        foreach (Pose pose in poses)
        {

            GameObject spawnedPrefab = (GameObject)PrefabUtility.InstantiatePrefab(spawnPrefab);
            Undo.RegisterCreatedObjectUndo(spawnedPrefab, "Spawn Objects");           
            spawnedPrefab.transform.position = pose.position;
            spawnedPrefab.transform.rotation = pose.rotation;


            

            
        }

        GenerateRandomPoints(); // update each time

    }

    void DuringSceneGUI(SceneView sceneView)
    {


        Handles.BeginGUI();

        Rect rect = new Rect(8, 8, 64, 64);

        foreach (GameObject p in prefabs)
        {

            Texture icon = AssetPreview.GetAssetPreview(p);

            if(GUI.Toggle(rect,spawnPrefab == p, new GUIContent( icon)))
            {
                spawnPrefab = p;
            }
            rect.y += rect.height + 2;
        }
       
        Handles.EndGUI();


        Handles.zTest = CompareFunction.LessEqual;

        Transform camTf = sceneView.camera.transform;

        if(Event.current.type == EventType.MouseMove)
        {
            Repaint();
        }

       

        bool holdingAlt = (Event.current.modifiers & EventModifiers.Alt) != 0;   

        //changing radius on scroll
        if(Event.current.type == EventType.ScrollWheel && !holdingAlt)
        {
            float scrollDirection = Mathf.Sign( Event.current.delta.y);

            so.Update();
            propRadius.floatValue *= 1 + scrollDirection * 0.05f;
            so.ApplyModifiedProperties();
            Repaint();
            Event.current.Use();
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

        //Ray ray = new Ray (camTf.position, camTf.forward);

        if(Physics.Raycast(ray, out RaycastHit hit))
        {
            //tangent space
            Vector3 hitNormal = hit.normal;
            Vector3 hitTangent = Vector3.Cross(hitNormal, camTf.up).normalized;
            Vector3 hitBitangent = Vector3.Cross(hitNormal, hitTangent);

            Ray GetTangentRay(Vector2 tangentSpacePos)
            {
                Vector3 rayOrigin = hit.point + (hitTangent * tangentSpacePos.x + hitBitangent * tangentSpacePos.y) * radius;
                rayOrigin += hitNormal * 2;
                Vector3 rayDirection = -hitNormal;

                return new Ray(rayOrigin, rayDirection);
            }

            List<Pose> hitPoses = new List<Pose>();

            //drawing pts
            foreach (RandomData rndDataPoint in randPoints)
            {
                Ray ptRay =  GetTangentRay(rndDataPoint.pointInDisc);
                //find point on surface             
                if(Physics.Raycast (ptRay, out RaycastHit ptHit))
                {
                    //rotation and assign pos
                    Quaternion ranDot = Quaternion.Euler(0f,0f, rndDataPoint.randAngleDeg);
                    Quaternion rot = Quaternion.LookRotation(ptHit.normal) * ( ranDot * Quaternion.Euler(90f, 0f, 0f));
                    Pose pose = new Pose(ptHit.point, rot);
                    hitPoses.Add(pose);

                    // draw sphere and normal
                    DrawSphere(ptHit.point);
                    Handles.DrawAAPolyLine(ptHit.point, ptHit.point + ptHit.normal);

                    //mesh

                    if(spawnPrefab != null)
                    {
                        Matrix4x4 poseToWorld = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);
                        MeshFilter[] filters = spawnPrefab.GetComponentsInChildren<MeshFilter>();
                        foreach (MeshFilter filter in filters)
                        {
                            Matrix4x4 childToPose = filter.transform.localToWorldMatrix;
                            Matrix4x4 childToWorld = poseToWorld * childToPose;

                            Mesh mesh = filter.sharedMesh;
                            Material mat = spawnPrefab.GetComponent<MeshRenderer>().sharedMaterial;
                            mat.SetPass(0);
                            Graphics.DrawMeshNow(mesh, childToWorld);

                        }
                    }
                                       
                }
                
            }

            // spawn on space press
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space)
            {
                TrySpawnObjects(hitPoses);
            }

            Handles.color = Color.red;
            Handles.DrawAAPolyLine(6, hit.point, hit.point + hitTangent);

            Handles.color = Color.green;
            Handles.DrawAAPolyLine(6, hit.point, hit.point + hitBitangent);

            Handles.color = Color.blue;
            Handles.DrawAAPolyLine(6, hit.point, hit.point + hitNormal);

            //draw circle adapted to terrain
            const int circleDetail = 128;
            Vector3[] ringPoints = new Vector3[circleDetail];
            for (int i = 0; i < circleDetail; i++)
            {
                float t = i / ((float)circleDetail-1);
                const float TAU = 6.28318530718f;
                float angRad = t * TAU;
                Vector2 dir = new Vector2 (Mathf.Cos(angRad), Mathf.Sin(angRad)); 
                Ray r  = GetTangentRay(dir);
                if(Physics.Raycast(r, out RaycastHit cHit))
                {
                    ringPoints[i] = cHit.point + cHit.normal * 0.02f;
                }
                else
                {
                    ringPoints[i] = r.origin;
                }
            }
            Handles.DrawAAPolyLine(ringPoints);

           // Handles.DrawWireDisc(hit.point, hit.normal, radius);

        }      

    }




}
