//
// Created by Necip Fazil Yildiran on 06/18/19.
//

#include <Eigen/Geometry>

#include <string>
#include <iterator>

#ifdef CXX_FILESYSTEM_HAVE_FS
# if CXX_FILESYSTEM_IS_EXPERIMENTAL
#   include <experimental/filesystem>
# else
#   include <filesystem>
# endif
#endif

#include "happly.h"

#define LINE_BUF_SIZE 100

using namespace std;
using namespace gr;

////////////////////////////////////////////////////////////////////////////////
/// Write
////////////////////////////////////////////////////////////////////////////////
template<typename PointRange,
         typename TextCoordRange,
         typename NormalRange,
         typename TrisRange,
         typename MTLSRange>
bool IOManager::WriteObject(
  const string& name,
  const PointRange &v,
  const TextCoordRange &tex_coords,
  const NormalRange &normals,
  const TrisRange &tris,
  const MTLSRange &mtls)
{
  std::string filename (name);

  bool haveExt = filename.at(filename.size()-4) == '.';

  if (tris.size() == 0){
    return WritePly(haveExt ?
                    filename.substr(0,filename.size()-3).append("ply") :
                    filename.append(".ply"),
                    v, normals);
  }
  else{
    return WriteObj(haveExt ?
                    filename.substr(0,filename.size()-3).append("obj") :
                    filename.append(".obj"),
                    v,
                    tex_coords,
                    normals,
                    tris,
                    mtls);
  }
}

template<typename PointRange, typename NormalRange>
bool
IOManager::WritePly(
  const std::string& filename,
  const PointRange &v,
  const NormalRange &normals)
{
    using Scalar = typename PointRange::value_type::Scalar;
    happly::PLYData plyOut;

    // Compute properties
    bool useNormals = normals.size() == v.size();
    // we check if we have colors by looking if the first rgb vector is void
    auto has_color = [](const typename PointRange::value_type& p ) { return p.hasColor(); };
    bool useColors = std::find_if(v.begin(), v.end(),has_color) != v.end();

    // Generate output buffers
    // Read all elements in data, correct their depth and print them in the file
    std::vector<Scalar>x,y,z,nx,ny,nz;
    std::vector<unsigned char>red, green, blue;
    typename NormalRange::const_iterator normal_it = normals.cbegin();
    for(const auto& p : v)
    {
        x.push_back( Scalar(p.pos()(0)) );
        y.push_back( Scalar(p.pos()(1)) );
        z.push_back( Scalar(p.pos()(2)) );

        if(useNormals) // size check is done earlier
        {
            nx.push_back( Scalar((*normal_it)(0)) );
            ny.push_back( Scalar((*normal_it)(1)) );
            nz.push_back( Scalar((*normal_it)(2)) );
            ++normal_it;
        }

        if(useColors)
        {
            red.push_back( static_cast<unsigned char>( p.rgb()(0) ) );
            green.push_back( static_cast<unsigned char>( p.rgb()(1) ) );
            blue.push_back( static_cast<unsigned char>( p.rgb()(2) ) );
        }
    }

    // generate output file structure and set buffers
    plyOut.addElement("vertex", v.size());
    auto& el = plyOut.getElement("vertex");
    el.template addProperty<Scalar>("x", x);
    el.template addProperty<Scalar>("y", y);
    el.template addProperty<Scalar>("z", z);

    if(useNormals) {
        el.template addProperty<Scalar>("nx", nx);
        el.template addProperty<Scalar>("ny", ny);
        el.template addProperty<Scalar>("nz", nz);
    }

    if(useColors) {
        el.template addProperty<unsigned char>("red", red);
        el.template addProperty<unsigned char>("green", green);
        el.template addProperty<unsigned char>("blue", blue);
    }

    plyOut.comments.push_back("Registered with OpenGR (https://github.com/STORM-IRIT/OpenGR/)");
    plyOut.write(filename, happly::DataFormat::Binary);

    return true;
}

template<typename PointRange,
         typename TexCoordRange,
         typename NormalRange,
         typename TrisRange,
         typename MTLSRange>
bool
IOManager::WriteObj(const std::string& filename,
         const PointRange &v,
         const TexCoordRange &tex_coords,
         const NormalRange &normals,
         const TrisRange &tris,
         const MTLSRange &mtls)
{
  std::fstream f(filename.c_str(), std::ios::out);
  if (!f || f.fail()) return false;
  size_t i;

  for(const auto& m : mtls)
  {
    f << "mtllib " << m << std::endl;
  }

  for(const auto& p : v)
  {
    f << "v "
      << p.pos()(0) << " " << p.pos()(1) << " " << p.pos()(2) << " ";

    if (p.rgb()(0) != 0) // TODO: What about hasColor?
      f << p.rgb()(0) << " " << p.rgb()(1) << " " << p.rgb()(2);

    f << std::endl;
  }

  for(const auto& n : normals)
  {
    f << "vn " << n(0) << " " << n(1) << " " << n(2)
      << std::endl;
  }

  for(const auto& t : tex_coords)
  {
    f << "vt " << t(0) << " " << t(1) << std::endl;
  }

  auto is_normals_empty = [&]() { return normals.begin() == normals.end(); };
  auto is_texcoords_empty = [&]() { return tex_coords.begin() == tex_coords.end(); };

  for(const auto& t : tris)
  {
    if(is_normals_empty() && is_texcoords_empty())
      f << "f " << t.a << " " << t.b << " " << t.c << std::endl;
    else if(!is_texcoords_empty())
      f << "f " << t.a << "/" << t.t1 << " " << t.b << "/"
        << t.t2 << " " << t.c << "/" << t.t3 << std::endl;
    else
      f << "f " << t.a << "/" << t.n1 << " " << t.b << "/"
        << t.n2 << " " << t.c << "/" << t.n3 << std::endl;
  }

  f.close();

  return true;
}

////////////////////////////////////////////////////////////////////////////////
/// Read
////////////////////////////////////////////////////////////////////////////////
template<typename Scalar>
bool
IOManager::ReadObject(const std::string& name,
           vector<Point3D<Scalar> > &v,
           vector<Eigen::Matrix2f> &tex_coords,
           vector<typename Point3D<Scalar>::VectorType> &normals,
           vector<tripple> &tris,
           vector<std::string> &mtls){
  string filename (name);

  if (filename.length() < 4) return false;

  string ext = filename.substr(filename.size()-3);

  if ( ext.compare ("ply") == 0 )
    return ReadPly<Scalar> (name, v, normals);
  if ( ext.compare ("obj") == 0 )
    return ReadObj<Scalar> (name, v, tex_coords, normals, tris, mtls);
  if ( ext.compare ("ptx") == 0 )
    return ReadPtx<Scalar> (name, v);

  std::cerr << "Unsupported file format" << std::endl;
  return false;
}

template<typename Scalar>
bool IOManager::ReadPtx(const std::string& filename, vector<Point3D<Scalar> > &vertex)
{
    fstream f(filename, ios::in);
    if (!f || f.fail()) {
        cerr << "(PTX) error opening file" << endl;
        return false;
    }


    int numOfVertices;
    int rows, cols;
    char line[LINE_BUF_SIZE];

    {
        f.getline(line,LINE_BUF_SIZE);
        std::stringstream ss(line); ss >> cols;
    }
    {
        f.getline(line,LINE_BUF_SIZE);
        std::stringstream ss(line); ss >> rows;
    }

    numOfVertices = cols*rows;

    // skip matrices declaration
    for(int i=0; i<8; i++) f.getline(line,LINE_BUF_SIZE);

    Point3D<Scalar> ptx;
    float intensity;
    typename Point3D<Scalar>::VectorType rgb;

    vertex.clear();
    vertex.reserve(numOfVertices);


    for (int i = 0; i < numOfVertices && ! f.eof(); i++) {
        f.getline(line,LINE_BUF_SIZE);
        std::stringstream ss(line);

        ss >> ptx.x();
        ss >> ptx.y();
        ss >> ptx.z();
        ss >> intensity;
        ss >> rgb(0);
        ss >> rgb(1);
        ss >> rgb(2);

        ptx.set_rgb(rgb);

        vertex.push_back( ptx );
    }

    f.close();

    return vertex.size() == numOfVertices;
}

template<typename Scalar>
bool
IOManager::ReadObj(const std::string& filename,
                   vector<Point3D<Scalar> > &v,
                   vector<Eigen::Matrix2f> &tex_coords,
                   vector<typename Point3D<Scalar>::VectorType> &normals,
                   vector<tripple> &tris,
                   vector<std::string> &mtls)
{

#ifdef CXX_FILESYSTEM_HAVE_FS
    const string workingDir { (CXX_FILESYSTEM_NAMESPACE::path(filename).parent_path() / "").string() };
#endif


  fstream f(filename, ios::in);
  if (!f || f.fail()) return false;
  char str[1024];
  float x, y, z;
  v.clear();
  tris.clear();
  while (!f.eof()) {
    f.getline(str, 1023);
    char ch[128];
    sscanf(str, "%s %*s", ch);
    if (strcmp(ch, "v") == 0) {
      sscanf(str, "%s %f %f %f", ch, &x, &y, &z);
      v.emplace_back(x, y, z);
      v[v.size() - 1].set_rgb(Point3D<Scalar>::VectorType::Zero());
    } else if (strcmp(ch, "vt") == 0) {
      Eigen::Matrix2f tex_coord;
      sscanf(str, "%s %f %f", ch, &tex_coord.coeffRef(0), &tex_coord.coeffRef(1));
      tex_coords.push_back(tex_coord);
    } else if (strcmp(ch, "vn") == 0) {
      typename Point3D<Scalar>::VectorType normal;
      sscanf(str, "%s %f %f %f", ch, &x, &y, &z);
      normal << x, y, z;
      normals.push_back(normal);
    } else if (strcmp(ch, "f") == 0) {
      tripple triangle;
      if (normals.size() && !tex_coords.size()) {
        sscanf(str, "%s %d//%d %d//%d %d//%d", ch, &(triangle.a),
               &(triangle.n1), &(triangle.b), &(triangle.n2), &(triangle.c),
               &(triangle.n3));
      } else if (normals.size() && tex_coords.size()) {
        sscanf(str, "%s %d/%d/%d %d/%d/%d %d/%d/%d", ch, &(triangle.a),
               &(triangle.t1), &(triangle.n1), &(triangle.b), &(triangle.t2),
               &(triangle.n2), &(triangle.c), &(triangle.t3), &(triangle.n3));
      } else if (!normals.size() && tex_coords.size()) {
        sscanf(str, "%s %d/%d %d/%d %d/%d", ch, &(triangle.a), &(triangle.t1),
               &(triangle.b), &(triangle.t2), &(triangle.c), &(triangle.t3));
      } else if (!normals.size() && !tex_coords.size()) {
        sscanf(str, "%s %d %d %d", ch, &(triangle.a), &(triangle.b),
               &(triangle.c));
      }
      tris.push_back(triangle);
      if (normals.size()) {
        v[triangle.a - 1].set_normal(normals[triangle.n1 - 1]);
        v[triangle.b - 1].set_normal(normals[triangle.n2 - 1]);
        v[triangle.c - 1].set_normal(normals[triangle.n3 - 1]);
      }
    } else if (strcmp(ch, "mtllib") == 0) {
#ifdef CXX_FILESYSTEM_HAVE_FS
        mtls.push_back(workingDir + std::string (str + 7));
#else
        std::cerr << "Skipping MTL (std::filesystem disabled)" << std::endl;
#endif
    }
  }
  f.close();

  if(tris.size() == 0){
    // In case we have vertex and normal lists but no face, assign normal to v
    if(v.size() == normals.size()){
      for (size_t i = 0; i < v.size(); ++i)
        v[i].set_normal(normals[i]);
    }
  }else {
    if (! normals.empty()){
      // If we have normals from faces, we must rebuild the normal array to duplicate
      // original normals and get a 1 to 1 correspondances with vertices
      // We assume that the normals have already been sent to vertices
      normals.clear();
      normals.reserve(v.size());

      for (unsigned int i = 0; i!= v.size(); i++)
        normals.push_back(v[i].normal());
    }
  }


#ifdef CXX_FILESYSTEM_HAVE_FS
  if (mtls.size()) {
    f.open(mtls[0].c_str(), ios::in);
    while (f && !f.fail()) {
      std::string img_name, dummy;
      f >> dummy;
      if (strcmp(dummy.c_str(), "map_Kd") == 0) {
        f >> img_name;

        // ... x = width, y = height, n = # 8-bit components per pixel ...
        int width,height,n;
        unsigned char *data = stbi_load_((workingDir + img_name).c_str(), &width, &height, &n, STBI_rgb);

        if (data != nullptr) {
            for (int i = 0; i < tris.size(); ++i) {
              const tripple &t = tris[i];
              Eigen::Matrix2f tc1 = tex_coords[t.t1 - 1];
              Eigen::Matrix2f tc2 = tex_coords[t.t2 - 1];
              Eigen::Matrix2f tc3 = tex_coords[t.t3 - 1];
              if ((tc1.array() < 1.0 && tc1.array() > 1.0 ).all() &&
                  (tc2.array() < 1.0 && tc2.array() > 1.0 ).all() &&
                  (tc3.array() < 1.0 && tc3.array() > 1.0 ).all()) {
              }
              auto setcolor = [data, width, height](Point3D<Scalar>& p, float u, float v) {
                  //using Scalar = typename Point3D<Scalar>::Scalar; // Scalar is already defined
                  unsigned char *ptr = (data + int(STBI_rgb) * int(v * height * width +  u * width));
                  int r = ptr[0];
                  int g = ptr[1];
                  int b = ptr[2];
                  p.set_rgb(Eigen::Matrix<int, 3, 1>(r,g,b).cast<Scalar>());
              };

              setcolor( v[t.a - 1], tc1.coeffRef(0), tc1.coeffRef(1) );
              setcolor( v[t.b - 1], tc2.coeffRef(0), tc2.coeffRef(1) );
              setcolor( v[t.c - 1], tc3.coeffRef(0), tc3.coeffRef(1) );
            }
            stbi_image_free_(data);
        } else {
            std::cerr << "Image loading failed: "
                      << stbi_failure_reason_() << "\n"
                      << "Path: " << workingDir + img_name
                      << std::endl;
        }
      }
    }
  }
#endif
  f.close();

  if (v.size() == 0) return false;
  return true;
}


template<typename Scalar>
bool
IOManager::ReadPly(const std::string& filename,
                   vector<Point3D<Scalar> > &v,
                   vector<typename Point3D<Scalar>::VectorType> &normals){


    // Construct a data object by reading from file
    happly::PLYData plyIn(filename);
    if( plyIn.hasElement("vertex") )
    {
        auto& el = plyIn.getElement("vertex");
        std::vector<std::string> names = el.getPropertyNames();

        std::vector<Scalar> x, y, z, nx, ny, nz;
        std::vector<unsigned char> red, green, blue;

        if( el.hasProperty("x") && el.hasProperty("y") && el.hasProperty("z") )
        {
            x = el.getProperty<Scalar>("x");
            y = el.getProperty<Scalar>("y");
            z = el.getProperty<Scalar>("z");
            v.resize( x.size() );
        }

        bool hasNormals = false;
        if( el.hasProperty("nx") && el.hasProperty("ny") && el.hasProperty("nz") )
        {
            nx = el.getProperty<Scalar>("nx");
            ny = el.getProperty<Scalar>("ny");
            nz = el.getProperty<Scalar>("nz");
            normals.resize( nx.size() );
            hasNormals = true;
        }

        bool hasColor = false;
        if( el.hasProperty("red") && el.hasProperty("green") && el.hasProperty("blue") )
        {
            red   = el.getProperty<unsigned char>("red");
            green = el.getProperty<unsigned char>("green");
            blue  = el.getProperty<unsigned char>("blue");
            hasColor = true;
        }

        const unsigned int size = v.size();
        using VectorType = typename Point3D<Scalar>::VectorType;
        for( unsigned int i = 0; i < size; ++i){
            auto& vv = v[i];
            vv.pos() = {x[i], y[i], z[i]};
            if( hasNormals ){
                vv.set_normal( {nx[i], ny[i], nz[i]} );
                normals[i] = vv.normal();
            }
            if( hasColor ){
                vv.set_rgb( {Scalar(red[i]), Scalar(green[i]), Scalar(blue[i])} );
            }
        }
    }
    else
        return false;

    return true;
}