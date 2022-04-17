#pragma once

#include <Eigen/Core>
#include "point_extlib2.hpp"

//! [extlib2::PointAdapter]
namespace extlib2
{

struct PointAdapter {
  public:
    enum {Dim = 3};
    typedef float Scalar;
    typedef Eigen::Matrix<Scalar, Dim, 1> VectorType;

  private:
    Eigen::Map< const VectorType > m_pos, m_normal, m_color;

  public:
    inline PointAdapter(const extlib2::PointType2& p)
      : m_pos   (Eigen::Map< const VectorType >( p.posBuffer + Dim*p.id )),
        m_normal(Eigen::Map< const VectorType >( p.nBuffer + Dim*p.id )),
        m_color (Eigen::Map< const VectorType >( p.colorBuffer + Dim*p.id ))
    { }

    inline const Eigen::Map< const VectorType >& pos()    const { return m_pos; }
    inline const Eigen::Map< const VectorType >& normal() const { return m_normal; }
    inline const Eigen::Map< const VectorType >& color()  const { return m_color; }
    inline const Eigen::Map< const VectorType >& rgb()  const { return m_color; }

    inline bool hasColor() const { return (m_color.array() > Scalar(0)).all(); } // invalid colors are encoded with -1

};

}
//! [extlib2::PointAdapter]
