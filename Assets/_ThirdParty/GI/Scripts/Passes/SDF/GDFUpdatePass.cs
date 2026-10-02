using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class GDFUpdatePass : Pass
    {
        private GDF _gdf;
        private SDFGrid _sdf_grid;

        private Kernel _karguments;
        private Kernel _kcompare;
        private Kernel _kcopy_hashes;
        private Kernel _kmanual;
        private Kernel _korder;
        private Kernel _kscroll;
        private Kernel _kupdate;

        public GDFUpdatePass(GDF gdf, SDFGrid sdf_grid)
        {
            _gdf = gdf;
            _sdf_grid = sdf_grid;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/Arguments"), "Main");
            _kcompare = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/Compare"), "Main");
            _kcopy_hashes = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/CopyHAshes"), "Main");
            _kmanual = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/Update"), "Manual");
            _korder = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/Order"), "Main");
            _kscroll = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/Scroll"), "Main");
            _kupdate = new Kernel(Resources.Load<ComputeShader>("Shaders/GDF/Update"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("GDF Update");

            //

            if (_gdf.IsDirty)
            {
                for (int i = 0; i < _gdf.CascadeCount; i++)
                    manual(cmd, data.Atlas, i,
                           0, 0, 0,
                           Defines.GDF_CASCADE_DIM, Defines.GDF_CASCADE_DIM, Defines.GDF_CASCADE_DIM);

                _sdf_grid.Parameters(cmd, _kcopy_hashes);
                _kcopy_hashes.Bind(cmd, P.HashesRW, _gdf.Hashes);
                _kcopy_hashes.DispatchEnoughFor(cmd, Defines.GDF_GRID_DIM, Defines.GDF_GRID_DIM, Defines.GDF_GRID_DIM);

                _gdf.IsDirty = false;
            }
            else
            {
                _gdf.Parameters(cmd, _kscroll);
                _sdf_grid.Parameters(cmd, _kscroll);

                _kscroll.BindOnce(cmd, P.GDFSDFRW, _gdf.SDF);
                _kscroll.BindOnce(cmd, P.GDFMapRW, _gdf.Map);

                for (int i = 0; i < _gdf.CascadeCount; i++)
                {
                    var amount = _gdf.CascadeOffsets[i];

                    if (amount.x != 0 || amount.y != 0 || amount.z != 0)
                    {
                        if ((Mathf.Abs(amount.x) > Defines.GDF_CASCADE_DIM / 2) ||
                            (Mathf.Abs(amount.y) > Defines.GDF_CASCADE_DIM / 2) ||
                            (Mathf.Abs(amount.z) > Defines.GDF_CASCADE_DIM / 2))
                        {
                            manual(cmd, data.Atlas, i,
                                   0, 0, 0,
                                   Defines.GDF_CASCADE_DIM, Defines.GDF_CASCADE_DIM, Defines.GDF_CASCADE_DIM);
                        }
                        else
                        {
                            int read;
                            int write;

                            _gdf.RequestScroll(i, out write, out read);

                            _kscroll.Seti(cmd, P.WriteIndex, write);
                            _kscroll.Seti(cmd, P.ReadIndex, read);

                            _kscroll.Seti(cmd, P.Offset, amount.x, amount.y, amount.z);

                            _kscroll.DispatchEnoughFor(cmd, Defines.GDF_CASCADE_DIM, Defines.GDF_CASCADE_DIM, Defines.GDF_CASCADE_DIM);

                            //

                            amount.x = up(amount.x);
                            amount.y = up(amount.y);
                            amount.z = up(amount.z);

                            int xtop = 0;
                            int xbottom = Defines.GDF_CASCADE_DIM;
                            int ytop = 0;
                            int ybottom = Defines.GDF_CASCADE_DIM;
                            int ztop = 0;
                            int zbottom = Defines.GDF_CASCADE_DIM;

                            if (amount.x < 0)
                            {
                                xtop -= amount.x;

                                manual(cmd, data.Atlas, i,
                                       0, ytop, ztop,
                                       -amount.x, ybottom - ytop, zbottom - ztop);
                            }
                            else if (amount.x > 0)
                            {
                                xbottom -= amount.x;

                                manual(cmd, data.Atlas, i,
                                       xbottom, ytop, ztop,
                                       amount.x, ybottom - ytop, zbottom - ztop);
                            }

                            if (amount.y < 0)
                            {
                                ytop -= amount.y;

                                manual(cmd, data.Atlas, i,
                                       xtop, 0, ztop,
                                       xbottom - xtop, -amount.y, zbottom - ztop);
                            }
                            else if (amount.y > 0)
                            {
                                ybottom -= amount.y;

                                manual(cmd, data.Atlas, i,
                                       xtop, ybottom, ztop,
                                       xbottom - xtop, amount.y, zbottom - ztop);
                            }

                            if (amount.z < 0)
                            {
                                ztop -= amount.z;

                                manual(cmd, data.Atlas, i,
                                       xtop, ytop, 0,
                                       xbottom - xtop, ybottom - ytop, -amount.z);
                            }
                            else if (amount.z > 0)
                            {
                                zbottom -= amount.z;

                                manual(cmd, data.Atlas, i,
                                       xtop, ytop, zbottom,
                                       xbottom - xtop, ybottom - ytop, amount.z);
                            }
                        }
                    }
                }

                //

                _gdf.Parameters(cmd, _kcompare);
                _sdf_grid.Parameters(cmd, _kcompare);

                _kcompare.Bind(cmd, P.Hashes, _gdf.Hashes2);
                _kcompare.Seti(cmd, P.Offset, _sdf_grid.Offset.x, _sdf_grid.Offset.y, _sdf_grid.Offset.z);

                _kcompare.Bind(cmd, P.HashesRW, _gdf.Hashes);
                _kcompare.Bind(cmd, P.ChangesRW, _gdf.Changes);

                _kcompare.DispatchEnoughFor(cmd, Defines.GDF_GRID_DIM, Defines.GDF_GRID_DIM, Defines.GDF_GRID_DIM);

                //

                _gdf.Parameters(cmd, _korder);
                _sdf_grid.Parameters(cmd, _korder);

                _korder.Bind(cmd, P.Changes, _gdf.Changes);

                _korder.Bind(cmd, P.CountRW, _gdf.UpdateCount);
                _korder.Bind(cmd, P.UpdatesRW, _gdf.Updates);

                int order_dim = Defines.GDF_CASCADE_DIM / _kupdate.GroupX;

                _korder.DispatchEnoughFor(cmd, order_dim * _gdf.CascadeCount, order_dim, order_dim);

                //

                _karguments.Bind(cmd, P.CountRW, _gdf.UpdateCount);
                _karguments.Bind(cmd, P.ArgumentsRW, _gdf.Arguments);
                _karguments.DispatchOne(cmd);

                //

                _gdf.Parameters(cmd, _kupdate);
                _sdf_grid.Parameters(cmd, _kupdate);

                _kupdate.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);
                _kupdate.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);
                _kupdate.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                _kupdate.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);

                _kupdate.Bind(cmd, P.DFInstances, data.Atlas.Buffer);

                _kupdate.Bind(cmd, P.Updates, _gdf.Updates);

                _kupdate.BindOnce(cmd, P.GDFSDFRW, _gdf.SDF);
                _kupdate.BindOnce(cmd, P.GDFMapRW, _gdf.Map);

                _kupdate.Dispatch(cmd, _gdf.Arguments);
            }

            //

            pass.End();
        }

        private int up(int amount)
        {
            int mult = 1;

            if (amount < 0)
            {
                amount = -amount;
                mult = -1;
            }

            return mult * _kmanual.GroupX * ((amount + _kmanual.GroupX - 1) / _kmanual.GroupX);
        }

        private void manual(CommandBuffer cmd, SDFAtlas atlas, int cascade, int x, int y, int z, int width, int height, int depth)
        {
            _gdf.Parameters(cmd, _kmanual);
            _sdf_grid.Parameters(cmd, _kmanual);

            _kmanual.BindOnce(cmd, P.SDFAtlas, atlas.SDF);
            _kmanual.BindOnce(cmd, P.VoxelAtlas, atlas.Voxels);
            _kmanual.Bind(cmd, P.SDFAssets, atlas.Assets);
            _kmanual.Bind(cmd, P.SDFBricks, atlas.Bricks);

            _kmanual.Bind(cmd, P.DFInstances, atlas.Buffer);

            _kmanual.BindOnce(cmd, P.GDFSDFRW, _gdf.SDF);
            _kmanual.BindOnce(cmd, P.GDFMapRW, _gdf.Map);

            _kmanual.Seti(cmd, P.WriteIndex, _gdf.CascadeIndices[cascade]);
            _kmanual.Seti(cmd, P.Cascade, cascade);
            _kmanual.Seti(cmd, P.Offset, up(x), up(y), up(z));

            _kmanual.DispatchEnoughFor(cmd, width, height, depth);
        }
    }
}