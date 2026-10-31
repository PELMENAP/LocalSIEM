import { useEffect, useRef } from 'react'
import * as echarts from 'echarts/core'
import { BarChart, LineChart, PieChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsCoreOption } from 'echarts/core'

echarts.use([BarChart, LineChart, PieChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer])

export const chartBase = {
  textStyle: { color: '#8b93a1', fontFamily: 'Inter, Segoe UI, system-ui, sans-serif', fontSize: 11 },
  tooltip: {
    backgroundColor: '#1d2128',
    borderColor: '#3a4150',
    textStyle: { color: '#d9dde4', fontSize: 12 }
  },
  axisLine: { lineStyle: { color: '#3a4150' } },
  splitLine: { lineStyle: { color: '#232831' } }
}

export function EChart({ option, height = 240 }: { option: EChartsCoreOption; height?: number | string }) {
  const ref = useRef<HTMLDivElement>(null)
  const chart = useRef<echarts.ECharts | null>(null)

  useEffect(() => {
    if (!ref.current) return
    const instance = echarts.init(ref.current, undefined, { renderer: 'canvas' })
    chart.current = instance
    const observer = new ResizeObserver(() => instance.resize())
    observer.observe(ref.current)
    return () => {
      observer.disconnect()
      instance.dispose()
      chart.current = null
    }
  }, [])

  useEffect(() => {
    chart.current?.setOption(option, { notMerge: true, lazyUpdate: true })
  }, [option])

  return <div ref={ref} style={{ height, width: '100%' }} />
}
