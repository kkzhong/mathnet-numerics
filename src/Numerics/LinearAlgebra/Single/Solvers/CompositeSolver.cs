// <copyright file="CompositeSolver.cs" company="Math.NET">
// Math.NET Numerics, part of the Math.NET Project
// http://numerics.mathdotnet.com
// http://github.com/mathnet/mathnet-numerics
// http://mathnetnumerics.codeplex.com
//
// Copyright (c) 2009-2013 Math.NET
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra.Solvers;
using MathNet.Numerics.Properties;

namespace MathNet.Numerics.LinearAlgebra.Single.Solvers
{
    /// <summary>
    /// A composite matrix solver. The actual solver is made by a sequence of
    /// matrix solvers. 
    /// </summary>
    /// <remarks>
    /// <para>
    /// Solver based on:<br />
    /// Faster PDE-based simulations using robust composite linear solvers<br />
    /// S. Bhowmicka, P. Raghavan a,*, L. McInnes b, B. Norris<br />
    /// Future Generation Computer Systems, Vol 20, 2004, pp 373�387<br />
    /// </para>
    /// <para>
    /// Note that if an iterator is passed to this solver it will be used for all the sub-solvers.
    /// </para>
    /// </remarks>
    public sealed class CompositeSolver : IIterativeSolver<float>
    {
        /// <summary>
        /// The collection of solvers that will be used
        /// </summary>
        readonly List<Tuple<IIterativeSolver<float>, IPreconditioner<float>>> _solvers;

        public CompositeSolver(IEnumerable<IIterativeSolverSetup<float>> solvers)
        {
            _solvers = solvers.Select(setup => new Tuple<IIterativeSolver<float>, IPreconditioner<float>>(setup.CreateSolver(), setup.CreatePreconditioner() ?? new UnitPreconditioner<float>())).ToList();
        }

        /// <summary>
        /// Solves the matrix equation Ax = b, where A is the coefficient matrix, b is the
        /// solution vector and x is the unknown vector.
        /// </summary>
        /// <param name="matrix">The coefficient matrix, <c>A</c>.</param>
        /// <param name="input">The solution vector, <c>b</c></param>
        /// <param name="result">The result vector, <c>x</c></param>
        public void Solve(Matrix<float> matrix, Vector<float> input, Vector<float> result, Iterator<float> iterator = null, IPreconditioner<float> preconditioner = null)
        {
            if (matrix.RowCount != matrix.ColumnCount)
            {
                throw new ArgumentException(Resources.ArgumentMatrixSquare, "matrix");
            }

            if (result.Count != input.Count)
            {
                throw new ArgumentException(Resources.ArgumentVectorsSameLength);
            }

            // Initialize the solver fields
            // Set the convergence monitor
            if (iterator == null)
            {
                iterator = new Iterator<float>(Iterator.CreateDefaultStopCriteria());
            }

            // Create a copy of the solution and result vectors so we can use them
            // later on
            var internalInput = input.Clone();
            var internalResult = result.Clone();

            foreach (var solver in _solvers)
            {
                // Store a reference to the solver so we can stop it.

                IterationStatus status;
                try
                {
                    // Reset the iterator and pass it to the solver
                    iterator.Reset();

                    // Start the solver
                    solver.Item1.Solve(matrix, internalInput, internalResult, iterator, solver.Item2);
                    status = iterator.Status;
                }
                catch (Exception)
                {
                    // The solver broke down. 
                    // Log a message about this
                    // Switch to the next preconditioner. 
                    // Reset the solution vector to the previous solution
                    input.CopyTo(internalInput);
                    continue;
                }

                // There was no fatal breakdown so check the status
                if (status == IterationStatus.Converged)
                {
                    // We're done
                    internalResult.CopyTo(result);
                    break;
                }

                // We're not done
                // Either:
                // - calculation finished without convergence
                if (status == IterationStatus.StoppedWithoutConvergence)
                {
                    // Copy the internal result to the result vector and
                    // continue with the calculation.
                    internalResult.CopyTo(result);
                }
                else
                {
                    // - calculation failed --> restart with the original vector
                    // - calculation diverged --> restart with the original vector
                    // - Some unknown status occurred --> To be safe restart.
                    input.CopyTo(internalInput);
                }
            }
        }

        /// <summary>
        /// Solves the matrix equation AX = B, where A is the coefficient matrix, B is the
        /// solution matrix and X is the unknown matrix.
        /// </summary>
        /// <param name="matrix">The coefficient matrix, <c>A</c>.</param>
        /// <param name="input">The solution matrix, <c>B</c>.</param>
        /// <param name="result">The result matrix, <c>X</c></param>
        public void Solve(Matrix<float> matrix, Matrix<float> input, Matrix<float> result, Iterator<float> iterator = null, IPreconditioner<float> preconditioner = null)
        {
            if (matrix.RowCount != input.RowCount || input.RowCount != result.RowCount || input.ColumnCount != result.ColumnCount)
            {
                throw Matrix.DimensionsDontMatch<ArgumentException>(matrix, input, result);
            }

            if (iterator == null)
            {
                iterator = new Iterator<float>(Iterator.CreateDefaultStopCriteria());
            }

            if (preconditioner == null)
            {
                preconditioner = new UnitPreconditioner<float>();
            }

            for (var column = 0; column < input.ColumnCount; column++)
            {
                var solution = Solve(matrix, input.Column(column), iterator, preconditioner);
                foreach (var element in solution.EnumerateNonZeroIndexed())
                {
                    result.At(element.Item1, column, element.Item2);
                }
            }
        }

        /// <summary>
        /// Solves the matrix equation Ax = b, where A is the coefficient matrix, b is the
        /// solution vector and x is the unknown vector.
        /// </summary>
        /// <param name="matrix">The coefficient matrix, <c>A</c>.</param>
        /// <param name="vector">The solution vector, <c>b</c>.</param>
        /// <returns>The result vector, <c>x</c>.</returns>
        public Vector<float> Solve(Matrix<float> matrix, Vector<float> vector, Iterator<float> iterator = null, IPreconditioner<float> preconditioner = null)
        {
            var result = new DenseVector(matrix.RowCount);
            Solve(matrix, vector, result, iterator, preconditioner);
            return result;
        }

        /// <summary>
        /// Solves the matrix equation AX = B, where A is the coefficient matrix, B is the
        /// solution matrix and X is the unknown matrix.
        /// </summary>
        /// <param name="matrix">The coefficient matrix, <c>A</c>.</param>
        /// <param name="input">The solution matrix, <c>B</c>.</param>
        /// <returns>The result matrix, <c>X</c>.</returns>
        public Matrix<float> Solve(Matrix<float> matrix, Matrix<float> input, Iterator<float> iterator = null, IPreconditioner<float> preconditioner = null)
        {
            var result = matrix.CreateMatrix(input.RowCount, input.ColumnCount);
            Solve(matrix, input, result, iterator, preconditioner);
            return result;
        }
    }
}