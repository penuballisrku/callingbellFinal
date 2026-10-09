import { useNavigate } from 'react-router';
import { useMutation } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { Button } from '@mui/material';
import VideocamRounded from '@mui/icons-material/VideocamRounded';
import { api, errorMessage } from '@/lib/api';

/** Opens the booking's private video room (it opens shortly before the start; the room page says when). */
export function JoinVideoButton({ bookingId, size = 'small' }: { bookingId: string; size?: 'small' | 'medium' }) {
  const navigate = useNavigate();
  const { enqueueSnackbar } = useSnackbar();
  const open = useMutation({
    mutationFn: () => api.get<string>(`/api/video/bookings/${bookingId}/room`),
    onSuccess: (roomId) => navigate(`/video/${roomId}`),
    onError: (e) => enqueueSnackbar(errorMessage(e), { variant: 'error' }),
  });
  return (
    <Button size={size} variant="contained" disableElevation startIcon={<VideocamRounded />} onClick={() => open.mutate()} disabled={open.isPending}
      sx={{ bgcolor: 'var(--cb-accent)', color: 'var(--cb-on-accent)', '&:hover': { bgcolor: 'var(--cb-accent)' } }}>
      Join video call
    </Button>
  );
}
