#include <fstream>
#include <iostream>
#include <string>

#import "Foundation/Foundation.h"
#include "gr/io/io.h"
#include "gr/utils/geometry.h"
#include "gr/utils/sampling.h"
#include "gr/algorithms/match4pcsBase.h"
#include "gr/algorithms/Functor4pcs.h"
#include "gr/algorithms/FunctorSuper4pcs.h"
#include "gr/algorithms/FunctorBrute4pcs.h"
#include <gr/algorithms/PointPairFilter.h>

#include <Eigen/Dense>


// #include "../demo-utils.h"

#define sqr(x) ((x) * (x))

using namespace std;
using namespace gr;
// using namespace gr::Demo;

// data IO
IOManager ioManager;

// static inline void printS4PCSParameterList(){
//     fprintf(stderr, "\t[ -r result_file_name (%s) ]\n", output.c_str());
//     fprintf(stderr, "\t[ -m output matrix file (%s) ]\n", outputMat.c_str());
//     fprintf(stderr, "\t[ -x (use 4pcs: false by default) ]\n");
//     fprintf(stderr, "\t[ --sampled1 (output sampled cloud 1 -- debug+super4pcs only) ]\n");
//     fprintf(stderr, "\t[ --sampled2 (output sampled cloud 2 -- debug+super4pcs only) ]\n");
// }
struct TransformVisitor {
    template <typename Derived>
    inline void operator()(
            float fraction,
            float best_LCP,
            const Eigen::MatrixBase<Derived>& /*transformation*/) const {
      if (fraction >= 0)
        {
          printf("done: %d%c best: %f                  \r",
               static_cast<int>(fraction * 100), '%', best_LCP);
          fflush(stdout);
        }
    }
    constexpr bool needsGlobalTransformation() const { return false; }
};

template <
    typename Matcher,
    typename PointType,
    typename Options,
    typename Range,
    template<typename> typename Sampler,
    typename TransformVisitor>
typename PointType::Scalar computeAlignment (
    const Options& options,
    const Utils::Logger& logger,
    const Range& P,
    const Range& Q,
    Eigen::Ref<Eigen::Matrix<typename PointType::Scalar, 4, 4>> mat,
    const Sampler<PointType>& sampler,
    TransformVisitor& visitor
    ) {
  Matcher matcher (options, logger);
  logger.Log<Utils::Verbose>( "Starting registration" );
  typename PointType::Scalar score = matcher.ComputeTransformation(P, Q, mat, sampler, visitor);


  logger.Log<Utils::Verbose>( "Score: ", score );
  logger.Log<Utils::Verbose>( "(Homogeneous) Transformation from ",
                              "Set2",
                              " to ",
                              "Set1",
                              ": \n",
                              mat);

//   if(! outputSampled1.empty() ){
//       logger.Log<Utils::Verbose>( "Exporting Sampled cloud 1 to ",
//                                   outputSampled1.c_str(),
//                                   " ..." );
//       ioManager.WriteObject((char *)outputSampled1.c_str(),
//                              matcher.getFirstSampled(),
//                              vector<Eigen::Matrix2f>(),
//                              vector<typename Point3D<float>::VectorType>(), // dummy
//                              vector<tripple>(),
//                              vector<string>());
//       logger.Log<Utils::Verbose>( "Export DONE" );
//   }
//   if(! outputSampled2.empty() ){
//       logger.Log<Utils::Verbose>( "Exporting Sampled cloud 2 to ",
//                                   outputSampled2.c_str(),
//                                   " ..." );
//       ioManager.WriteObject((char *)outputSampled2.c_str(),
//                              matcher.getSecondSampled(),
//                              vector<Eigen::Matrix2f>(),
//                              vector<typename Point3D<float>::VectorType>(), // dummy
//                              vector<tripple>(),
//                              vector<string>());
//       logger.Log<Utils::Verbose>( "Export DONE" );
//   }

  return score;
}

extern "C" {




void NativeJunk_SayHello() {
    NSLog (@"Hello, from NativeJunk 2!\n");
}




int32_t OpenGRMain(const float *set1Data, int32_t set1NumPoints, float *set2Data, int32_t set2NumPoints, float *outputMat, float *outputScore) {
  using namespace gr;
  using Scalar = float;
  // Point clouds are read as gr::Point3D, then converted to other types if necessary to
  // emulate PointAdapter usage
  vector<Point3D<Scalar> > set1, set2;
  vector<Eigen::Matrix2f> tex_coords1, tex_coords2;
  vector<typename Point3D<Scalar>::VectorType> normals1, normals2;
  vector<tripple> tris1, tris2;
  vector<std::string> mtls1, mtls2;

  // Match and return the score (estimated overlap or the LCP).
  typename Point3D<Scalar>::Scalar score = 0;

  constexpr Utils::LogLevel loglvl = Utils::Verbose;

  using TrVisitorType = typename std::conditional <loglvl==Utils::NoLog,
                            DummyTransformVisitor,
                            TransformVisitor>::type;
  using PairFilter = gr::AdaptivePointFilter;

  TrVisitorType visitor;
  Utils::Logger logger(loglvl);

  // prepare matcher ressourcesoutputSampled2
  using MatrixType = Eigen::Matrix<typename Point3D<Scalar>::Scalar, 4, 4>;
  MatrixType mat (MatrixType::Identity());

  // Read the inputs.
  for (int i = 0; i < set1NumPoints; i++) {
      if (i < 10) {
          NSLog(@"IN1: %f %f %f\n", set1Data[i*3], set1Data[i*3+1], set1Data[i*3+2]);
      }
    set1.push_back(Point3D<Scalar>(set1Data[i*3], set1Data[i*3+1], set1Data[i*3+2]));
  }
  for (int i = 0; i < set2NumPoints; i++) {
      if (i < 10) {
          NSLog(@"IN2: %f %f %f\n", set2Data[i*3], set2Data[i*3+1], set2Data[i*3+2]);
      }
    set2.push_back(Point3D<Scalar>(set2Data[i*3], set2Data[i*3+1], set2Data[i*3+2]));
  }

  // clean only when we have pset to avoid wrong face to point indexation
  if (tris1.size() == 0)
    Utils::CleanInvalidNormals(set1, normals1);
  if (tris2.size() == 0)
    Utils::CleanInvalidNormals(set2, normals2);

  try {
      using PointType    = gr::Point3D<Scalar>;
      using MatcherType  = gr::Match4pcsBase<gr::FunctorSuper4PCS, PointType, 
                                             TrVisitorType, gr::AdaptivePointFilter,
                                             gr::AdaptivePointFilter::Options>;
      using OptionType   = typename MatcherType::OptionsType;

      UniformDistSampler<PointType> sampler;

      OptionType options;

      score = computeAlignment<MatcherType, PointType> (options, logger, set1, set2,
                                                        mat, sampler, visitor);
  }
  catch (const std::exception& e) {
      logger.Log<Utils::ErrorReport>( "[Error]: " , e.what() );
      logger.Log<Utils::ErrorReport>( "Aborting with code -3 ..." );
      return -3;
  }
  catch (...) {
      logger.Log<Utils::ErrorReport>( "[Unknown Error]: Aborting with code -4 ..." );
      return -4;
  }


    // Copy output matrix
    for (int i = 0; i < 4; i++) {
        for (int j = 0; j < 4; j++) {
            outputMat[i*4+j] = mat(i,j);
        }
    }

    Utils::TransformPointCloud(set2, mat);
    // Copy set2 back to set2data
    for (int i = 0; i < set2NumPoints; i++) {
        set2Data[i*3] = set2[i].x();
        set2Data[i*3+1] = set2[i].y();
        set2Data[i*3+2] = set2[i].z();
    }

    *outputScore = score;

  return 0;
}





}

