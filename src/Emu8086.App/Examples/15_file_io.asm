; Files: create a file on the virtual drive C:, write text, read it back and print it.
; The virtual drive is Documents\emu8086ln\vdrive\C.
#make_COM#
org 100h

    mov ah, 3Ch             ; create file (CX = attributes)
    mov cx, 0
    mov dx, offset filename
    int 21h
    jc failed
    mov bx, ax              ; BX = file handle

    mov ah, 40h             ; write CX bytes from DS:DX
    mov cx, text_len
    mov dx, offset text
    int 21h

    mov ah, 3Eh             ; close
    int 21h

    mov ah, 3Dh             ; open for reading (AL = 0)
    mov al, 0
    mov dx, offset filename
    int 21h
    jc failed
    mov bx, ax

    mov ah, 3Fh             ; read up to 100 bytes
    mov cx, 100
    mov dx, offset buffer
    int 21h
    mov si, ax
    mov buffer[si], '$'     ; terminate what was read

    mov ah, 3Eh
    int 21h

    mov dx, offset buffer
    mov ah, 09h
    int 21h
    ret
failed:
    mov dx, offset error
    mov ah, 09h
    int 21h
    ret

filename db 'test.txt', 0
text     db 'This text went to a file and came back.'
text_len equ $ - text
error    db 'File error!$'
buffer   db 101 dup(?)
