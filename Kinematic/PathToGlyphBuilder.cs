using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace Kinematic
{
    interface IGlyphPath
    {
        Vector2 Lerp(float t);
    }

    class LinePath : IGlyphPath
    {
        public Vector2 Lerp(float t)
        {
            return Vector2.Lerp(_start, _end, t);
        }

        Vector2 _end;
        Vector2 _start;
        public LinePath(Vector2 start, Vector2 end)
        {
            _end = end;
            _start = start;
        }
    }

    class CubicBezierPath : IGlyphPath
    {
        public Vector2 Lerp(float t)
        {
            float inv_t = 1 - t;
            return inv_t * inv_t * inv_t * _points[0] + 3 * t * inv_t * inv_t * _points[1] + 3 * t * t * inv_t * _points[2] + t * t * t * _points[3];
        }

        Vector2[] _points;
        public CubicBezierPath(Vector2[] points)
        {
            if (points.Length != 4)
                throw new ArgumentException(string.Format("Not enough points: {0}", _points.Length));

            _points = points;
        }
    }

    class QuadraticBezierPath : IGlyphPath
    {
        public Vector2 Lerp(float t)
        {
            float inv_t = 1 - t;
            return inv_t * inv_t * _points[0] + 2 * t * inv_t * _points[1] + t * t * _points[2];
        }

        Vector2[] _points;
        public QuadraticBezierPath(Vector2[] points)
        {
            if (points.Length != 3)
                throw new ArgumentException(string.Format("Not enough points: {0}", _points.Length));
            _points = points;
        }
    }

    class TextGlyph : List<IGlyphPath>
    {

    }

    class PathToGlyphBuilder : ICanvasPathReceiver
    {
        Vector2 _startPoint = Vector2.Zero;
        Vector2 _lastPoint = Vector2.Zero;
        TextGlyph _currentGlyph = new TextGlyph();
        List<TextGlyph> allGlyphs = new List<TextGlyph>();

        public List<TextGlyph> TextGlyphs
        { get { return allGlyphs; } }

        public void AddArc(Vector2 endPoint, float radiusX, float radiusY, float rotationAngle, CanvasSweepDirection sweepDirection, CanvasArcSize arcSize)
        {
            System.Diagnostics.Debug.WriteLine(string.Format("Arc: {0}:{1}:{2}", endPoint, radiusX, radiusY));
        }

        public void AddCubicBezier(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 endPoint)
        {
            _currentGlyph.Add(new CubicBezierPath(new []{ _lastPoint, controlPoint1, controlPoint2, endPoint}));
            _lastPoint = endPoint;
            System.Diagnostics.Debug.WriteLine(string.Format("Cubic: {0}:{1}:{2}", controlPoint1,  controlPoint2, endPoint));
        }

        public void AddLine(Vector2 endPoint)
        {
            _currentGlyph.Add(new LinePath(_lastPoint, endPoint));
            _lastPoint = endPoint;
            System.Diagnostics.Debug.WriteLine(string.Format("Line: {0}", endPoint));
        }

        public void AddQuadraticBezier(Vector2 controlPoint, Vector2 endPoint)
        {
            _currentGlyph.Add(new CubicBezierPath(new[] { _lastPoint, controlPoint, endPoint }));
            _lastPoint = endPoint;
            System.Diagnostics.Debug.WriteLine(string.Format("Bezier: {0}:{1}", controlPoint, endPoint));
        }

        public void BeginFigure(Vector2 startPoint, CanvasFigureFill figureFill)
        {
            _lastPoint = startPoint;
            _startPoint = startPoint;
            System.Diagnostics.Debug.WriteLine("<<<<<");
        }

        public void EndFigure(CanvasFigureLoop figureLoop)
        {
            if (figureLoop == CanvasFigureLoop.Closed)
                _currentGlyph.Add(new LinePath(_lastPoint, _startPoint));
            allGlyphs.Add(_currentGlyph);
            // start a new block
            _currentGlyph = new TextGlyph();

            System.Diagnostics.Debug.WriteLine(">>>>>");
        }

        public void SetFilledRegionDetermination(CanvasFilledRegionDetermination filledRegionDetermination)
        {
            
        }

        public void SetSegmentOptions(CanvasFigureSegmentOptions figureSegmentOptions)
        {
         
        }
    }
}
