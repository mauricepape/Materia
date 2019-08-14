﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using Materia.Nodes.Attributes;
using Materia.Textures;
using System.Threading;
using NLog;

namespace Materia.Nodes.Atomic
{
    public class GraphInstanceNode : ImageNode
    {
        private static ILogger Log = LogManager.GetCurrentClassLogger();

        public Graph GraphInst { get; protected set; }

        CancellationTokenSource ctk;

        protected string path;
        protected Dictionary<string, object> jsonParameters;
        protected Dictionary<string, object> jsonCustomParameters;
        protected Dictionary<string, GraphParameterValue> nameMap;
        protected int randomSeed;
        protected bool updatingParams;

        protected bool loading;

        [Editable(ParameterInputType.GraphFile, "Materia Graph File", "Content")]
        public string GraphFilePath
        {
            get
            {
                return path;
            }
            set
            {
                path = value;
                Load(path);
                Updated();
            }
        }

        [Editable(ParameterInputType.Map, "Parameters", "Instance Parameters")]
        public Dictionary<string, GraphParameterValue> Parameters
        {
            get
            {
                if(GraphInst != null)
                {
                    return GraphInst.Parameters;
                }

                return null;
            }
        }

        [Editable(ParameterInputType.Map, "Custom Parameters", "Instance Parameters")]
        public List<GraphParameterValue> CustomParameters
        {
            get
            {
                if(GraphInst != null)
                {
                    return GraphInst.CustomParameters;
                }

                return null;
            }
        }

        public int RandomSeed
        {
            get
            {
                if(GraphInst != null)
                {
                    return GraphInst.RandomSeed;
                }

                return 0;
            }
            set
            {
                GraphInst.RandomSeed = value;
            }
        }

        public new float TileX
        {
            get
            {
                return tileX;
            }
            set
            {
                tileX = value;
            }
        }

        public new float TileY
        {
            get
            {
                return tileY;
            }
            set
            {
                tileY = value;
            }
        }

        public new GraphPixelType InternalPixelFormat
        {
            get
            {
                return internalPixelType;
            }
            set
            {
                internalPixelType = value;
            }
        }

        protected string GraphData { get; set; }

        public GraphInstanceNode(int w, int h, GraphPixelType p = GraphPixelType.RGBA)
        {
            width = w;
            height = h;

            nameMap = new Dictionary<string, GraphParameterValue>();

            Id = Guid.NewGuid().ToString();

            tileX = tileY = 1;

            Name = "Graph Instance";

            this.path = "";

            //we do not initialize the inputs and outputs here
            //instead they are loaded after the graph is loaded
            Inputs = new List<NodeInput>();
            Outputs = new List<NodeOutput>();
        }

        public GraphParameterValue GetCustomParameter(string name)
        {
            GraphParameterValue v = null;
            nameMap.TryGetValue(name, out v);
            return v;
        }

        private void GraphParameterValue_OnGraphParameterUpdate(GraphParameterValue param)
        {
            if(GraphInst != null && !updatingParams && param.ParentGraph == GraphInst)
            {
                TryAndProcess();
            }
        }

        public override void TryAndProcess()
        {
            if (!Async)
            {
                PrepareProcess();
                if (GraphInst != null)
                {
                    GraphInst.TryAndProcess();
                }
                TryAndReleaseBuffers();
                return;
            }

            //if (ctk != null)
            //{
            //    ctk.Cancel();
            //}

            //ctk = new CancellationTokenSource();

            //Task.Delay(25, ctk.Token).ContinueWith(t =>
            //{
            //    if (t.IsCanceled) return;

                if (ParentGraph != null)
                {
                    ParentGraph.Schedule(this);
                }
            //}, Context);
        }

        private void PrepareProcess()
        {
            if (GraphInst != null)
            {
                //handle assignment of upper parameter reassignment
                Graph p = ParentGraph;
                updatingParams = true;
                if (p != null)
                {
                    foreach (var k in Parameters.Keys)
                    {
                        if (Parameters[k].IsFunction()) continue;

                        string[] split = k.Split('.');

                        if (p.HasParameterValue(split[0], split[1]))
                        {
                            var realParam = Parameters[k];
                            realParam.AssignValue(p.GetParameterValue(split[0], split[1]));
                        }
                    }

                    int count = CustomParameters.Count;
                    for(int i = 0; i < count; i++)
                    {
                        var param = CustomParameters[i];
                        if (p.HasParameterValue(Id, param.Name))
                        {
                            param.AssignValue(p.GetParameterValue(Id, param.Name));
                        }
                    }
                }
                updatingParams = false;
            }
        }

        public override Task GetTask()
        {
            return Task.Factory.StartNew(() =>
            {
                PrepareProcess();
            })
            .ContinueWith(t =>
            {
                if (GraphInst != null)
                {
                    GraphInst.TryAndProcess();
                }
            }, Context)
            .ContinueWith(t =>
            {
                while (GraphInst != null && GraphInst.IsProcessing)
                {
                    Thread.Sleep(1);
                }
            })
            .ContinueWith(t =>
            {
                TryAndReleaseBuffers();
            }, Context);
        }

        //used for initial loading
        //not used for restoring from .materia
        public bool Load(string path)
        {
            if(GraphInst != null)
            {
                GraphData = null;
                GraphInst.OnGraphParameterUpdate -= GraphParameterValue_OnGraphParameterUpdate;
                GraphInst.Dispose();
                GraphInst = null;
            }

            nameMap = new Dictionary<string, GraphParameterValue>();

            if (File.Exists(path) && Path.GetExtension(path).ToLower().Contains("mtg"))
            {
                loading = true;
                this.path = path;

                string nm = Path.GetFileNameWithoutExtension(path);

                Name = nm;

                //the width and height here don't matter
                GraphInst = new Graph(nm);

                GraphData = File.ReadAllText(path);
                GraphInst.AssignParentNode(this);
                GraphInst.Synchronized = !Async;
                GraphInst.FromJson(GraphData);
                GraphInst.AssignParameters(jsonParameters);
                GraphInst.AssignCustomParameters(jsonCustomParameters);
     
                GraphInst.AssignSeed(randomSeed);

                //now do real initial resize
                GraphInst.ResizeWith(width, height);

                //mark as readonly
                GraphInst.ReadOnly = true;

                GraphInst.OnGraphParameterUpdate += GraphParameterValue_OnGraphParameterUpdate;

                //setup inputs and outputs
                Setup();
                loading = false;
                return true;
            }
            else
            {
                this.path = null;
            }

            return false;
        }

        void Setup()
        {
            int count = 0;
            if(GraphInst.InputNodes.Count > 0)
            {
                count = GraphInst.InputNodes.Count;
                for(int i = 0; i < count; i++)
                {
                    string id = GraphInst.InputNodes[i];
                    Node n;
                    if (GraphInst.NodeLookup.TryGetValue(id, out n))
                    {
                        InputNode inp = (InputNode)n;
                        NodeInput np = new NodeInput(NodeType.Color | NodeType.Gray, this, inp.Name);

                        inp.SetInput(np);
                        Inputs.Add(np);
                    }
                }
            }

            if(GraphInst.OutputNodes.Count > 0)
            {
                count = GraphInst.OutputNodes.Count;
                for(int i = 0; i < count; i++)
                {
                    string id = GraphInst.OutputNodes[i];
                    Node n;
                    if (GraphInst.NodeLookup.TryGetValue(id, out n))
                    {
                        OutputNode op = (OutputNode)n;

                        NodeOutput ot;

                        ot = new NodeOutput(NodeType.Color | NodeType.Gray, n, op.Name);
                        //we add to our graph instance outputs so things can actually connect 
                        //to the output
                        Outputs.Add(ot);
                        op.SetOutput(ot);

                        n.OnUpdate += N_OnUpdate;
                    }
                }
            }

            //name map used in parameter mapping for quicker lookup
            count = GraphInst.CustomParameters.Count;
            for(int i = 0; i < count; i++)
            {
                var param = GraphInst.CustomParameters[i];
                nameMap[param.Name] = param;
            }
        }

        private void N_OnUpdate(Node n)
        {
            Updated();
        }

        void TryAndReleaseBuffers()
        {
            if (GraphInst != null)
            {
                GraphInst.ReleaseIntermediateBuffers();
            }
        }

        public override byte[] GetPreview(int width, int height)
        {
            //we only show the first output as preview
            if(Outputs.Count > 0)
            {
                return Outputs[0].Node.GetPreview(width, height);
            }

            return null;
        }

        public override GLTextuer2D GetActiveBuffer()
        {
            if(Outputs.Count > 0)
            {
                return Outputs[0].Node.GetActiveBuffer();
            }

            return null;
        }

        //we actually store the graph raw data
        //so this file can be transported without needing
        //the original graph file
        public class GraphInstanceNodeData : NodeData
        {
            public List<string> inputIds;
            public Dictionary<string, object> parameters;
            public Dictionary<string, object> customParameters;
            public int randomSeed;
            public string rawData;
            public string path;
        }

        public override void FromJson(string data)
        {
            GraphInstanceNodeData d = JsonConvert.DeserializeObject<GraphInstanceNodeData>(data);
            SetBaseNodeDate(d);
            GraphData = d.rawData;
            path = d.path;
            jsonParameters = d.parameters;
            jsonCustomParameters = d.customParameters;
            randomSeed = d.randomSeed;
            

            bool didLoad = false;

            //we do this incase 
            //the original graph was updated
            //and thus we should pull it in
            //if it exists
            //otherwise we fall back on
            //last saved graph data

            if(File.Exists(path))
            {
                didLoad = Load(path);
            }

            //if path not found or could not load
            //fall back to last instance data saved
            if (!didLoad)
            {
                nameMap = new Dictionary<string, GraphParameterValue>();
                loading = true;
                GraphInst = new Graph(Name);
                GraphInst.AssignParentNode(this);
                GraphInst.Synchronized = !Async;
                GraphInst.FromJson(GraphData);
                GraphInst.AssignParameters(jsonParameters);
                GraphInst.AssignCustomParameters(jsonCustomParameters);
                GraphInst.AssignSeed(randomSeed);
                GraphInst.ResizeWith(width, height);
                GraphInst.OnGraphParameterUpdate += GraphParameterValue_OnGraphParameterUpdate;

                Setup();
                loading = false;
            }
        }

        public override string GetJson()
        {
            GraphInstanceNodeData d = new GraphInstanceNodeData();
            FillBaseNodeData(d);
            d.rawData = GraphData;
            d.path = path;
            d.parameters = GraphInst.GetConstantParameters();
            d.customParameters = GraphInst.GetCustomParameters();
            d.randomSeed = RandomSeed;

            return JsonConvert.SerializeObject(d);
        }

        protected override void OnWidthHeightSet()
        {
            if(GraphInst != null)
            {
                GraphInst.ResizeWith(width, height);
                GraphInst.TryAndProcess();
            }

            Updated();
        }

        public override void Dispose()
        {
            base.Dispose();

            if(GraphInst != null)
            {
                GraphInst.OnGraphParameterUpdate -= GraphParameterValue_OnGraphParameterUpdate;
                GraphInst.Dispose();
                GraphInst = null;
            }
        }
    }
}
