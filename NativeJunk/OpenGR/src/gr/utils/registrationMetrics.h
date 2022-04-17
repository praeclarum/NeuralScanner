// Copyright 2020 Nicolas Mellado
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// -------------------------------------------------------------------------- //
//
// Authors: Nicolas Mellado
//
// This file is part of the OpenGR library
//

#pragma once

#include "gr/accelerators/kdtree.h"
#include <Eigen/Core> // Eigen::Ref

namespace gr{
namespace Utils{

/// \brief Implementation of the Largest Common PointSet metric
///
template <typename Scalar>
struct LCPMetric {
    /// Support size of the LCP
    Scalar epsilon_ = (std::numeric_limits<Scalar>::max)();

    template <typename Range>
    inline Scalar operator()( const gr::KdTree<Scalar> & ref,
                              const Range& target,
                              const Eigen::Ref<const Eigen::Matrix<Scalar, Eigen::Dynamic, Eigen::Dynamic>>& mat,
                              Scalar terminate_value = Scalar( 0 ))
    {
        using RangeQuery = typename gr::KdTree<Scalar>::template RangeQuery<>;

        std::atomic_uint good_points(0);

        const size_t number_of_points = target.size();
        const size_t terminate_int_value = terminate_value * number_of_points;
        const Scalar sq_eps = epsilon_*epsilon_;

        for (size_t i = 0; i < number_of_points; ++i) {

            // Use the kdtree to get the nearest neighbor
            RangeQuery query;
            query.queryPoint = (mat * target[i].pos().homogeneous()).template head<3>();
            query.sqdist     = sq_eps;

            if ( ref.doQueryRestrictedClosestIndex( query ).first != gr::KdTree<Scalar>::invalidIndex() ) {
                good_points++;
            }

            // We can terminate if there is no longer chance to get better than terminate_value
            if (number_of_points - i + good_points < terminate_int_value) { break; }
        }
        return Scalar(good_points) / Scalar(number_of_points);
    }
};

/// \brief Implementation of a weighted variant of the Largest Common PointSet metric.
///
template <typename Scalar>
struct WeightedLCPMetric {
    /// Support size of the LCP
    Scalar epsilon_ = (std::numeric_limits<Scalar>::max)();

    template <typename Range>
    inline Scalar operator()( const gr::KdTree<Scalar> & ref,
                              const Range& target,
                              const Eigen::Ref<const Eigen::Matrix<Scalar, Eigen::Dynamic, Eigen::Dynamic>>& mat,
                              Scalar terminate_value = Scalar( 0 ))
    {
        using RangeQuery = typename gr::KdTree<Scalar>::template RangeQuery<>;

        std::atomic<Scalar> good_points(0);

        auto kernel = [](Scalar x) {
            return std::pow(std::pow(x,4) - Scalar(1), 2);
        };

        auto computeWeight = [kernel](Scalar sqx, Scalar th) {
            return kernel( std::sqrt(sqx) / th );
        };

        const size_t number_of_points = target.size();
        const size_t terminate_int_value = terminate_value * number_of_points;
        const Scalar sq_eps = epsilon_*epsilon_;

        for (size_t i = 0; i < number_of_points; ++i) {

            // Use the kdtree to get the nearest neighbor
            RangeQuery query;
            query.queryPoint = (mat * target[i].pos().homogeneous()).template head<3>();
            query.sqdist     = sq_eps;

            auto result = ref.doQueryRestrictedClosestIndex( query );

            if ( result.first != gr::KdTree<Scalar>::invalidIndex() ) {
                assert (result.second <= query.sqdist);
                good_points = good_points + computeWeight(result.second, epsilon_);
            }

            // We can terminate if there is no longer chance to get better than terminate_value
            if (number_of_points - i + good_points < terminate_int_value) { break; }
        }
        return Scalar(good_points) / Scalar(number_of_points);
    }
};

} // namespace Utils
} // namespace gr


