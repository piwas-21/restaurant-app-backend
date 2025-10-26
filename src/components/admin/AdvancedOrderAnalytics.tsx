import React, { useMemo, useState } from 'react';
import { OrderDto } from '@/types/order';
import {
  LineChart,
  Line,
  BarChart,
  Bar,
  PieChart,
  Pie,
  Cell,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from 'recharts';
import { TrendingUp, PieChart as PieChartIcon, BarChart3, Calendar } from 'lucide-react';
import styles from './AdvancedOrderAnalytics.module.css';

interface AdvancedOrderAnalyticsProps {
  orders: OrderDto[];
}

export default function AdvancedOrderAnalytics({ orders }: AdvancedOrderAnalyticsProps) {
  const [dateRange, setDateRange] = useState<'7days' | '30days' | '90days'>('30days');

  // Calculate date range
  const { startDate, endDate } = useMemo(() => {
    const end = new Date();
    const start = new Date();

    switch (dateRange) {
      case '7days':
        start.setDate(end.getDate() - 7);
        break;
      case '30days':
        start.setDate(end.getDate() - 30);
        break;
      case '90days':
        start.setDate(end.getDate() - 90);
        break;
    }

    return { startDate: start, endDate: end };
  }, [dateRange]);

  // Filter orders by date range
  const filteredOrders = useMemo(() => {
    return orders.filter(order => {
      if (!order.createdAt) return false;
      const orderDate = new Date(order.createdAt);
      return orderDate >= startDate && orderDate <= endDate;
    });
  }, [orders, startDate, endDate]);

  // Prepare data for orders over time chart
  const ordersOverTime = useMemo(() => {
    const groupedByDate: { [key: string]: { date: string; orders: number; revenue: number } } = {};

    filteredOrders.forEach(order => {
      if (!order.createdAt) return;
      const date = new Date(order.createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
      if (!groupedByDate[date]) {
        groupedByDate[date] = { date, orders: 0, revenue: 0 };
      }
      groupedByDate[date].orders += 1;
      groupedByDate[date].revenue += order.total;
    });

    return Object.values(groupedByDate).sort((a, b) =>
      new Date(a.date).getTime() - new Date(b.date).getTime()
    );
  }, [filteredOrders]);

  // Prepare data for order types pie chart
  const orderTypeData = useMemo(() => {
    const types: { [key: string]: number } = {};

    filteredOrders.forEach(order => {
      const type = order.type || 'Unknown';
      types[type] = (types[type] || 0) + 1;
    });

    return Object.entries(types).map(([name, value]) => ({ name, value }));
  }, [filteredOrders]);

  // Prepare data for revenue by status bar chart
  const revenueByStatus = useMemo(() => {
    const statusRevenue: { [key: string]: number } = {};

    filteredOrders.forEach(order => {
      const status = order.status || 'Unknown';
      statusRevenue[status] = (statusRevenue[status] || 0) + order.total;
    });

    return Object.entries(statusRevenue).map(([status, revenue]) => ({
      status,
      revenue: parseFloat(revenue.toFixed(2)),
    }));
  }, [filteredOrders]);

  // Colors for charts - RUMI theme colors
  const COLORS = ['#c00000', '#e06666', '#10b981', '#f59e0b', '#8b5cf6', '#ec4899', '#06b6d4'];

  // Format currency
  const formatCurrency = (value: number) => {
    return new Intl.NumberFormat('en-CH', {
      style: 'currency',
      currency: 'CHF',
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(value);
  };

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <h2 className={styles.title}>
          <BarChart3 size={24} />
          Advanced Analytics
        </h2>
        <div className={styles.dateRangeButtons}>
          <button
            className={dateRange === '7days' ? styles.active : ''}
            onClick={() => setDateRange('7days')}
          >
            <Calendar size={16} />
            Last 7 Days
          </button>
          <button
            className={dateRange === '30days' ? styles.active : ''}
            onClick={() => setDateRange('30days')}
          >
            <Calendar size={16} />
            Last 30 Days
          </button>
          <button
            className={dateRange === '90days' ? styles.active : ''}
            onClick={() => setDateRange('90days')}
          >
            <Calendar size={16} />
            Last 90 Days
          </button>
        </div>
      </div>

      <div className={styles.chartsGrid}>
        {/* Orders Over Time Line Chart */}
        <div className={styles.chartCard}>
          <div className={styles.chartHeader}>
            <TrendingUp size={20} />
            <h3>Orders & Revenue Over Time</h3>
          </div>
          <div className={styles.chartContainer}>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={ordersOverTime}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                <XAxis
                  dataKey="date"
                  stroke="#64748b"
                  style={{ fontSize: '12px' }}
                />
                <YAxis
                  yAxisId="left"
                  stroke="#3b82f6"
                  style={{ fontSize: '12px' }}
                />
                <YAxis
                  yAxisId="right"
                  orientation="right"
                  stroke="#10b981"
                  style={{ fontSize: '12px' }}
                />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#1e293b',
                    border: 'none',
                    borderRadius: '8px',
                    color: '#f1f5f9',
                  }}
                  formatter={(value: number, name: string) => {
                    if (name === 'revenue') return [formatCurrency(value), 'Revenue'];
                    return [value, 'Orders'];
                  }}
                />
                <Legend />
                <Line
                  yAxisId="left"
                  type="monotone"
                  dataKey="orders"
                  stroke="#c00000"
                  strokeWidth={2}
                  dot={{ fill: '#c00000', r: 4 }}
                  activeDot={{ r: 6 }}
                />
                <Line
                  yAxisId="right"
                  type="monotone"
                  dataKey="revenue"
                  stroke="#10b981"
                  strokeWidth={2}
                  dot={{ fill: '#10b981', r: 4 }}
                  activeDot={{ r: 6 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Order Types Pie Chart */}
        <div className={styles.chartCard}>
          <div className={styles.chartHeader}>
            <PieChartIcon size={20} />
            <h3>Order Types Distribution</h3>
          </div>
          <div className={styles.chartContainer}>
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={orderTypeData}
                  cx="50%"
                  cy="50%"
                  labelLine={false}
                  label
                  outerRadius={100}
                  fill="#8884d8"
                  dataKey="value"
                >
                  {orderTypeData.map((_, index) => (
                    <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#1e293b',
                    border: 'none',
                    borderRadius: '8px',
                    color: '#f1f5f9',
                  }}
                />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Revenue by Status Bar Chart */}
        <div className={styles.chartCard}>
          <div className={styles.chartHeader}>
            <BarChart3 size={20} />
            <h3>Revenue by Order Status</h3>
          </div>
          <div className={styles.chartContainer}>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={revenueByStatus}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                <XAxis
                  dataKey="status"
                  stroke="#64748b"
                  style={{ fontSize: '12px' }}
                />
                <YAxis
                  stroke="#64748b"
                  style={{ fontSize: '12px' }}
                  tickFormatter={formatCurrency}
                />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#1e293b',
                    border: 'none',
                    borderRadius: '8px',
                    color: '#f1f5f9',
                  }}
                  formatter={(value: number) => [formatCurrency(value), 'Revenue']}
                />
                <Bar dataKey="revenue" fill="#c00000" radius={[8, 8, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>
    </div>
  );
}
